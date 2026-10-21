using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>Copies borrowed selected PCM into bounded 200ms transport packets.</summary>
public sealed class StreamingAsrChunker
{
 readonly float[] pending; int count; long next,packetStart; uint sequence;
 readonly Action<byte[]> emit;
 public long NextSample=>next; public uint NextSequence=>sequence;
 public StreamingAsrChunker(int rate,long start,int milliseconds,Action<byte[]> output)
 {pending=new float[Math.Max(1,rate*milliseconds/1000)];next=packetStart=start;emit=output;}
 public void Append(float[] pcm,int length,long start)
 {
  if(start!=next)throw new InvalidOperationException("PCM sample gap/overlap");
  for(int i=0;i<length;i++){pending[count++]=pcm[i];next++;if(count==pending.Length)Flush();}
 }
 public void Flush()
 {
  if(count==0)return;
  using(var memory=new MemoryStream(16+count*2))using(var writer=new BinaryWriter(memory))
  {writer.Write(sequence++);writer.Write(packetStart);writer.Write((uint)count);for(int i=0;i<count;i++)writer.Write((short)Math.Round(Math.Max(-1,Math.Min(1,pending[i]))*32767));emit(memory.ToArray());}
  packetStart=next;count=0;
 }
}
/// <summary>No Unity API on socket threads. Main thread consumes notices and guards turn identity.</summary>
public sealed class StreamingAsrSession
{
 sealed class Packet{public byte[] Bytes;public WebSocketMessageType Type;}
 readonly ClientWebSocket socket=new ClientWebSocket(); readonly CancellationTokenSource cancel=new CancellationTokenSource();
 readonly ConcurrentQueue<Packet> packets=new ConcurrentQueue<Packet>();readonly SemaphoreSlim available=new SemaphoreSlim(0);
 readonly TaskCompletionSource<string> final=new TaskCompletionSource<string>();
 public readonly ConcurrentQueue<JObject> Notices=new ConcurrentQueue<JObject>();
 public readonly TurnContext Context;public readonly StreamingAsrChunker Chunker;
 public Task<string> Final=>final.Task;public string FailureReason{get;private set;} public bool Ended{get;private set;}
 int queued;uint sent;readonly string startMessage;readonly Uri uri;
 public StreamingAsrSession(TurnContext context,string url,string capture,int rate,long first,int chunkMs,string language)
 {
  Context=context;uri=new Uri(url);
  socket.Options.SetRequestHeader("X-Trace-Id",context.TraceId);socket.Options.SetRequestHeader("X-Turn-Id",context.TurnId.ToString());socket.Options.SetRequestHeader("X-Asr-Language-Mode",context.LanguageCode);
  startMessage=new JObject{["type"]="start",["trace_id"]=context.TraceId,["turn_id"]=context.TurnId,["capture_id"]=capture,["sample_rate"]=rate,["start_sample_index"]=first,["language_mode"]=context.LanguageCode,["language_hint"]=language??""}.ToString();
  Chunker=new StreamingAsrChunker(rate,first,chunkMs,bytes=>Queue(bytes,WebSocketMessageType.Binary));
  _=Task.Run(Run);
 }
 void Queue(byte[] bytes,WebSocketMessageType type)
 {
  if(final.Task.IsCompleted)return;
  if(Interlocked.Increment(ref queued)>30){Fail("transport_backlog_limit");return;}
  packets.Enqueue(new Packet{Bytes=bytes,Type=type});available.Release();
 }
 public void Finish()
 {
  if(Ended)return;Ended=true;Chunker.Flush();Queue(Encoding.UTF8.GetBytes(new JObject{["type"]="end_of_utterance",["next_sequence_id"]=Chunker.NextSequence,["end_sample_index"]=Chunker.NextSample}.ToString()),WebSocketMessageType.Text);
 }
 public void Abort(string reason){FailureReason=reason;cancel.Cancel();socket.Abort();final.TrySetResult(null);}
 void Fail(string reason){Notices.Enqueue(new JObject{["type"]="transport_error",["reason"]=reason});Abort(reason);}
 async Task Run()
 {
  try
  {
   using(var connectTimeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
   {connectTimeout.CancelAfter(5000);await socket.ConnectAsync(uri,connectTimeout.Token);}
   await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(startMessage)),WebSocketMessageType.Text,true,cancel.Token);
   var receiver=ReceiveGuarded();
   while(!final.Task.IsCompleted)
   {
    await available.WaitAsync(cancel.Token);
    while(packets.TryDequeue(out var packet))
    {
     Interlocked.Decrement(ref queued);await socket.SendAsync(new ArraySegment<byte>(packet.Bytes),packet.Type,true,cancel.Token);
     if(packet.Type==WebSocketMessageType.Binary&&++sent%5==0)Notices.Enqueue(new JObject{["type"]="chunks_sent",["chunk_count"]=sent});
     if(packet.Type==WebSocketMessageType.Text){await receiver;if(socket.State==WebSocketState.Open||socket.State==WebSocketState.CloseReceived){using(var timeout=new CancellationTokenSource(2000))await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"final_received",timeout.Token);}break;}
    }
   }
  }
  catch(Exception e){if(!final.Task.IsCompleted)Fail(e.GetType().Name);}
  finally{final.TrySetResult(null);cancel.Cancel();socket.Dispose();}
 }
 async Task ReceiveGuarded(){try{await Receive();}catch(Exception e){if(!final.Task.IsCompleted)Fail(e.GetType().Name);}}
 async Task Receive()
 {
  var buffer=new byte[8192];
  while(!cancel.IsCancellationRequested)
  {
   using(var message=new MemoryStream())
   {
    WebSocketReceiveResult result;
    do{result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),cancel.Token);if(result.MessageType==WebSocketMessageType.Close){Fail("connection_closed_before_final");return;}message.Write(buffer,0,result.Count);if(message.Length>65536){Fail("message_capacity");return;}}while(!result.EndOfMessage);
    var raw=Encoding.UTF8.GetString(message.ToArray());var json=JObject.Parse(raw);string type=(string)json["type"];
    if(type!="error"&&((string)json["trace_id"]!=Context.TraceId||(long?)json["turn_id"]!=Context.TurnId)){Fail("response_identity_mismatch");return;}
    if(type=="error"){Fail((string)json["reason"]??"decode_error");return;}
    if(type=="final"){final.TrySetResult(raw);return;}
    Notices.Enqueue(json);
   }
  }
 }
}
