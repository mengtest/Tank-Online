﻿using System;
using Msg;
using System.Collections;
using System.Threading;
using System.Collections.Generic;
using unity = UnityEngine;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf;

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

public class RpcClient
{
    private unity.WaitForFixedUpdate fixedUpdate;
	public Rpc.RpcClient client;
	private GameManager Gm;
	private CancellationTokenSource rpcStopSignal;
	private Msg.Input m_Input;
	public RpcClient(Rpc.RpcClient client, GameManager Gm)
	{
		rpcStopSignal = new CancellationTokenSource ();
		this.client = client;
		this.Gm = Gm;
        fixedUpdate = new unity.WaitForFixedUpdate();
	}

	/// <summary>
	/// Blocking unary call example.  Calls GetFeature and prints the response.
	/// </summary>
	public  bool Login(string name,string pwsd)
	{
		LoginInput user = new LoginInput{ UserName = name, Pswd = pwsd };
		UserInfo userInfo = client.Login(user);
		Gm.SetUser (userInfo, true);
		unity.Debug.Log ("Login :" + userInfo);
		if( userInfo.UserName != "" ){
			CallClient(userInfo.Uuid);
			return true;
		}
		/*
		if (userInfo.Error == "") {
			CallClient();
			unity.Debug.Log("async succeed");
			return true;
		}
		*/
		return false;
	}
	public void CreateAccount(string name,string pwsd ,string email){
		RegistInput userReigister = new RegistInput{ UserName = name, Pswd = pwsd, Email = email };
		Error err = client.CreateAccount (userReigister);
		unity.Debug.Log ("CreateAccount :" + err);
	}

	public async void CallClient(Int64 userId){
		
		using (var call = client.CallMethod ()) {
			// Recevice
			var CallRecvTask = Task.Run (async () => {
				while (!rpcStopSignal.Token.IsCancellationRequested && await call.ResponseStream.MoveNext ()) {
					CallFuncInfo f = call.ResponseStream.Current;
					unity.Debug.Log (f);
					Gm.CallMathod (f);
				}
			});
			//Send

			CallFuncInfo start = new CallFuncInfo{ FromId = userId};
			await call.RequestStream.WriteAsync (start);
            CallFuncInfo cali = new CallFuncInfo { FromId = userId, TimeStamp = util.GetTimeStamp(), Func = "Calibrate" };
            await call.RequestStream.WriteAsync(cali);
            while (!rpcStopSignal.Token.IsCancellationRequested) {
				CallFuncInfo f = new CallFuncInfo();
				if (Gm.OutFuncQueue.TryPeek(out f)){
					unity.Debug.Log (f);
                    try
                    {
                        call.RequestStream.WriteAsync(f);
                        Gm.OutFuncQueue.TryDequeue(out f);
                    }
					catch (InvalidOperationException e)
                    {
                        unity.Debug.Log("CallMathod,"+f+","+e);
                    }
				}
                await Task.Delay(1);
            }

            await call.RequestStream.CompleteAsync ();
			await CallRecvTask;
			unity.Debug.Log ("callMethod(); exit");
		}
	}
	public async void SyncPos(long userId){
		unity.Debug.Log ("start SynPos");
		using (var call = client.SyncPos ()) {
			var CallRecvTask = Task.Run (async () => {
				while (!rpcStopSignal.Token.IsCancellationRequested && await call.ResponseStream.MoveNext ()) {
					Msg.Position pos = call.ResponseStream.Current;
					Gm.InPosQueue.Enqueue(pos);
				}
			});

			Msg.Input start = new Msg.Input{ UserId = userId};
			await call.RequestStream.WriteAsync (start);
            while (!rpcStopSignal.Token.IsCancellationRequested) {
				if (Gm.OutInputQueue.TryPeek(out m_Input)){
                    try {
					    call.RequestStream.WriteAsync (m_Input);
                        Gm.OutInputQueue.TryDequeue(out m_Input);
                    }
                    catch (InvalidOperationException e)
                    {
                        unity.Debug.Log("SyncPos input," + m_Input +","+ e);
                    }
                }
                await Task.Delay(1);
            }

			await call.RequestStream.CompleteAsync ();
			await CallRecvTask;
			unity.Debug.Log ("SyncPos(); exit");
		}
	}
	public async void Stop(){
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