using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class StreamingTtsSession
{
 readonly ClientWebSocket socket=new ClientWebSocket();readonly CancellationTokenSource cancel=new CancellationTokenSource();
 public readonly AudioPcmRingBuffer Pcm=new AudioPcmRingBuffer(32000);
 public readonly ConcurrentQueue<JObject> Notices=new ConcurrentQueue<JObject>();
 public long ProfileChunks,ProfileLastArrivalTicks,ProfileMaxGapTicks,ProfileBackpressureTicks;
 public volatile bool Ended;public volatile string Failure;
 readonly JObject start;readonly Uri uri;
 public StreamingTtsSession(SpeechRequest r,string url)
 {
  uri=new Uri(url);socket.Options.SetRequestHeader("X-Trace-Id",r.traceId);socket.Options.SetRequestHeader("X-Turn-Id",r.turnId.ToString());
  start=new JObject{["trace_id"]=r.traceId,["turn_id"]=r.turnId,["speech_request_id"]=r.requestId,["text"]=r.text,["language"]=r.language,["style"]=r.style};
  _=Task.Run(Run);
 }
 public void Abort(){Pcm.Clear();cancel.Cancel();try{socket.Abort();}catch(ObjectDisposedException){};}
 async Task Run()
 {
  try
  {
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){deadline.CancelAfter(5000);await socket.ConnectAsync(uri,deadline.Token);}
   await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(start.ToString())),WebSocketMessageType.Text,true,cancel.Token);
   var buffer=new byte[8192];bool began=false,first=true;
   while(!cancel.IsCancellationRequested)
   {
    using(var memory=new MemoryStream())
    {
     WebSocketReceiveResult reply;
     do{reply=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),cancel.Token);if(reply.MessageType==WebSocketMessageType.Close)throw new IOException("missing_eos");memory.Write(buffer,0,reply.Count);if(memory.Length>2000000)throw new IOException("frame_capacity");}while(!reply.EndOfMessage);
     if(reply.MessageType==WebSocketMessageType.Text)
     {
      var e=JObject.Parse(Encoding.UTF8.GetString(memory.ToArray()));string type=(string)e["type"];
      if(type=="error")throw new IOException((string)e["reason"]);
      if((string)e["trace_id"]!=(string)start["trace_id"]||(long?)e["turn_id"]!=(long?)start["turn_id"]||(string)e["speech_request_id"]!=(string)start["speech_request_id"])throw new IOException("identity");
      if(type=="start"){if((int?)e["sample_rate"]!=32000||(int?)e["channels"]!=1||(string)e["format"]!="pcm16")throw new IOException("format");began=true;}
      else if(type=="end"){if((long?)e["total_frames"]!=Pcm.Received)throw new IOException("sample_count");Pcm.End();Ended=true;Notices.Enqueue(e);break;}
      else throw new IOException("event");
     }
     else
     {
      if(!began||memory.Length%2!=0)throw new IOException("pcm_frame");byte[] bytes=memory.ToArray();var pcm=new float[bytes.Length/2];
      for(int i=0;i<pcm.Length;i++)pcm[i]=(short)(bytes[i*2]|(bytes[i*2+1]<<8))/32768f;
      if(pcm.Length>Pcm.Capacity)throw new IOException("ring_capacity");
      bool profile=AudioPcmRingBuffer.ProfilingEnabled;long arrival=profile?System.Diagnostics.Stopwatch.GetTimestamp():0;
      if(profile){long previous=Interlocked.Exchange(ref ProfileLastArrivalTicks,arrival);Interlocked.Increment(ref ProfileChunks);if(previous>0)ProfileMaxGapTicks=Math.Max(ProfileMaxGapTicks,arrival-previous);}
      while(Pcm.Free<pcm.Length)await Task.Delay(5,cancel.Token);
      if(profile)Interlocked.Add(ref ProfileBackpressureTicks,System.Diagnostics.Stopwatch.GetTimestamp()-arrival);
      if(cancel.IsCancellationRequested||!Pcm.Write(pcm,arrival))break;
      if(first){first=false;Notices.Enqueue(new JObject{["type"]="first_chunk",["utc_ms"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["sample_count"]=pcm.Length});}
     }
    }
   }
  }
  catch(Exception e){if(!cancel.IsCancellationRequested){Failure=e.GetType().Name;Notices.Enqueue(new JObject{["type"]="error",["reason"]=Failure});}}
  finally{socket.Dispose();}
 }
}
