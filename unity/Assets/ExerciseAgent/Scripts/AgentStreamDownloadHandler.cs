using System;
using System.Collections.Concurrent;
using System.Text;
using UnityEngine.Networking;

/// <summary>Bounded NDJSON events; no raw LLM tokens or Unity objects on receive.</summary>
public sealed class AgentStreamDownloadHandler : DownloadHandlerScript
{
    public readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
    readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    readonly char[] characters = new char[8192];
    readonly StringBuilder line = new StringBuilder();
    public string Error { get; private set; }
    int total;
    public AgentStreamDownloadHandler() : base(new byte[8192]) { }
    protected override bool ReceiveData(byte[] data,int length)
    {
        if(data==null||length==0)return true;
        total+=length;
        if(total>131072||Events.Count>64){Error="stream_capacity";return false;}
        int count=decoder.GetChars(data,0,length,characters,0,false);
        for(int i=0;i<count;i++)
        {
            char c=characters[i];
            if(c=='\n'){if(line.Length>0)Events.Enqueue(line.ToString());line.Clear();}
            else line.Append(c);
            if(line.Length>65536){Error="event_capacity";return false;}
        }
        return true;
    }
    protected override void CompleteContent()
    {if(line.Length>0)Error="incomplete_event";}
}
