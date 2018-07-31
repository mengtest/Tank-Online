﻿using System;
using Msg;
using System.Collections;
using System.Threading;
using System.Collections.Generic;
using unity = UnityEngine;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf;

using System.Collections.Concurrent;


public class util{
    private static Int64 timeOffset = 0;

    public static long TimeOffset
    {
        get
        {
            return timeOffset;
        }

        set
        {
            timeOffset = value;
        }
    }

    public static Int64 GetTimeStamp(){
		var timeStamp = Convert.ToInt64 ((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds) + timeOffset;
		return timeStamp;
	}

	public static Tuple<unity.Vector3,unity.Quaternion> PraseTransform(TransForm t){
		unity.Quaternion q = new unity.Quaternion{x = -1*(float)(t.Rotation.X), y=-1*(float)(t.Rotation.Z), z = -1*(float)(t.Rotation.Y), w = (float)(t.Rotation.W)};
		unity.Vector3 p = new unity.Vector3{x = (float)(t.Position.X) , y=(float)(t.Position.Z) , z=(float)(t.Position.Y)};
		return new Tuple<unity.Vector3,unity.Quaternion>(p,q);
	}
}


public class AgentRpc
{
    private unity.WaitForFixedUpdate fixedUpdate;
    private ClientToAgent.ClientToAgentClient client;
    //Queue
    private CancellationTokenSource rpcStopSignal;
    private Msg.Input m_Input;
    private Metadata metadata;
    private ServerInfo gameServerInfo;

    public ConcurrentQueue<RoomList> RoomListQueue;
    public ConcurrentQueue<HomeView> HomeViewQueue;
    public ConcurrentQueue<RoomContent> RoomContentQueue;
    public AgentRpc(string addr)
    {
        rpcStopSignal = new CancellationTokenSource();
        fixedUpdate = new unity.WaitForFixedUpdate();
        var channel = new Channel(addr, ChannelCredentials.Insecure);
        client = new ClientToAgent.ClientToAgentClient(channel);

        metadata = new Metadata();
        var sessionkey = client.AquireSessionKey(new Empty());
        unity.Debug.Log(sessionkey.Value);
        metadata.Add("session-id", sessionkey.Value);

    }
    public async void Stop()
    {
        rpcStopSignal.Cancel();
        await GrpcEnvironment.ShutdownChannelsAsync();
    }

    /// <summary>
    /// Blocking unary call example.  Calls GetFeature and prints the response.
    /// </summary>
    public UserInfo Login(string name, string pwsd)
    {
        LoginInput user = new LoginInput { UserName = name, Pswd = pwsd };
        UserInfo userInfo = client.Login(user, metadata);
        unity.Debug.Log("Login :" + userInfo);
        metadata.Add("uname", userInfo.UserName);
        return userInfo;
    }

    public void RegistAccount(string name, string pwsd, string email)
    {
        RegistInput userReigister = new RegistInput { UserName = name, Pswd = pwsd, Email = email };
        Error err = client.CreateAccount(userReigister, metadata);
        unity.Debug.Log("CreateAccount :" + err);
    }

    public GameRpc GetGameServer()
    {
        gameServerInfo = client.AquireGameServer(new Empty(), metadata);
        var gameSever = new GameRpc(gameServerInfo, this.metadata);
        return gameSever;
    }

    public async void UpdateRoomList()
    {
        using (var call = client.UpdateRoomList(new Empty(), metadata))
        {
            // Recevice
            while (!rpcStopSignal.Token.IsCancellationRequested && await call.ResponseStream.MoveNext())
            {
                RoomList roomList = call.ResponseStream.Current;
                RoomListQueue.Enqueue(roomList);
                //Updata to 
            }
        };
    }
    public bool EnterRoom(long uuid) 
    {
        var success = client.JoinRoom(new ID { Value = uuid },metadata);
        if (success.Ok){
            UpdateRoomContent();
            return true;
        }
        return false;
    }
    public bool CreatRoom(RoomSetting setting)
    {
        var s = client.CreateRoom(setting,metadata);
        if (s.Ok) {
            UpdateRoomContent();
            return true;
        }
        return false;
    }

    public async void UpdateRoomContent()
    {
        using (var call = client.UpdateRoomContent(new Empty(), metadata))
        {
            // Recevice
            while (!rpcStopSignal.Token.IsCancellationRequested && await call.ResponseStream.MoveNext())
            {
                RoomContent roomContent = call.ResponseStream.Current;
                RoomContentQueue.Enqueue(roomContent);
                //Updata to 
            }
        };
    }
}


public class GameRpc
{
    private unity.WaitForFixedUpdate fixedUpdate;
	public ClientToGame.ClientToGameClient client;

	private CancellationTokenSource rpcStopSignal;
	private Msg.Input m_Input;
    private Metadata metadata;
	public GameRpc(ServerInfo info,Metadata metadata)
	{
		rpcStopSignal = new CancellationTokenSource ();
        this.metadata = metadata;
        
        var channel = new Channel(info.Addr, ChannelCredentials.Insecure);
        this.client = new ClientToGame.ClientToGameClient(channel);
        fixedUpdate = new unity.WaitForFixedUpdate();
	}
    public async void Stop()
    {
        rpcStopSignal.Cancel();
        await GrpcEnvironment.ShutdownChannelsAsync();
    }
}



public static class AsyncEnumerator
{
	public static Task<bool> MoveNext<T>(this IAsyncEnumerator<T> enumerator)
	{
		if (enumerator == null) 
			throw new ArgumentNullException (nameof (enumerator));

		return enumerator.MoveNext (CancellationToken.None);
	}
} 