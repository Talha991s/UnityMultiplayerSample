﻿using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System.Collections.Generic;
using System;
using System.Text;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;

    private Dictionary<string, NetworkObjects.NetworkPlayer> clientList = new Dictionary<string, NetworkObjects.NetworkPlayer>();
    private Dictionary<string, float> clientHeartBeat = new Dictionary<string, float>();

    float lastplayerInfo = 0.0f;
    float delaytosendplayerinfo = 0.02f;
    float deltaplayercolor = 0.0f;
    float delayinplayerColor = 1.0f;

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c)
    {
        
        Debug.Log("Accepted a connection");

        PlayerUpdateMsg iDee = new PlayerUpdateMsg();
        iDee.cmd = Commands.PLAYER_ID;
        iDee.player.id = c.InternalId.ToString();
        Assert.IsTrue(c.IsCreated);
        SendToClient(JsonUtility.ToJson(iDee), c);

        //send to server now

        ServerUpdateMsg spawnedPlayers = new ServerUpdateMsg();
        spawnedPlayers.cmd = Commands.SPAWNEDPLAYER;

        foreach(KeyValuePair<string, NetworkObjects.NetworkPlayer>element in clientList)
        {
            spawnedPlayers.players.Add(element.Value);
        }
        Assert.IsTrue(c.IsCreated);
        SendToClient(JsonUtility.ToJson(spawnedPlayers), c);

        PlayerUpdateMsg NewPlayer = new PlayerUpdateMsg();
        NewPlayer.cmd = Commands.NEWPLAYERSPAWNING;
        NewPlayer.player.id = c.InternalId.ToString();
        
        for(int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(NewPlayer), m_Connections[i]);
        }

        m_Connections.Add(c);
        clientList[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();
        //// Example to send a handshake message:
        //HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i,NetworkConnection client)
    {
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received!");
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player "+ puMsg.player.id+"  update message received!");
             UpdateClientInfo(puMsg);
            break;
            case Commands.SERVER_UPDATE:
            ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
            Debug.Log("Server update message received!");
            break;
            default:
            Debug.Log("SERVER ERROR: Unrecognized message received!");
            break;
        }
    }

    void OnDisconnect(int i){
        Debug.Log("Client"+m_Connections[i].InternalId.ToString()+" disconnected from server");
        m_Connections[i] = default(NetworkConnection);
    }



    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i, m_Connections[i]);
                    clientHeartBeat[m_Connections[i].InternalId.ToString()] = Time.time;
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
        if(Time.time - lastplayerInfo >= delaytosendplayerinfo)
        {
            lastplayerInfo = Time.time;

            SendPlayerInfoToClient();
        }

        if(Time.time - deltaplayercolor >= delayinplayerColor)
        {
            deltaplayercolor = Time.time;
            ChangeColorOfClient();
        }
        HeartBeatCheck();
    }

    void SendPlayerInfoToClient()
    {
        ServerUpdateMsg message = new ServerUpdateMsg();

        foreach(KeyValuePair<string, NetworkObjects.NetworkPlayer> element in  clientList)
        {
            message.players.Add(element.Value);
        }

        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(message), m_Connections[i]);
        }
    }

    void UpdateClientInfo(PlayerUpdateMsg puMsg)
    {
        if(clientList.ContainsKey(puMsg.player.id))
        {
            clientList[puMsg.player.id].id = puMsg.player.id;
            clientList[puMsg.player.id].cubPos = puMsg.player.cubPos;
        }
    }

    void ChangeColorOfClient()
    {
        Debug.Log("Change Color");
        foreach(KeyValuePair<string, NetworkObjects.NetworkPlayer> element in clientList)
        {
            element.Value.cubeColor = new Color(UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f));

        }
    }

    void HeartBeatCheck()
    {
        List<string> deleteList = new List<string>();
        foreach(KeyValuePair<string,float> element in clientHeartBeat)
        {
            if(Time.time - element.Value >= 5.0f)
            {
                Debug.Log(element.Key.ToString() + "HeartBeat Disconnected");
                deleteList.Add(element.Key);
            }
        }
        if(deleteList.Count != 0)
        {
            for (int i = 0; i < deleteList.Count; i++)
            {
                clientList.Remove(deleteList[i]);
                clientHeartBeat.Remove(deleteList[i]);
            }
            DisconnectedPlayerMsg disconnectMessage = new DisconnectedPlayerMsg();
            disconnectMessage.disconnectedPlayer = deleteList;

            for(int i = 0; i < m_Connections.Length; i++)
            {
                if(deleteList.Contains(m_Connections[i].InternalId.ToString()) == true)
                {
                    continue;
                }

                Assert.IsTrue(m_Connections[i].IsCreated);
                SendToClient(JsonUtility.ToJson(disconnectMessage), m_Connections[i]);
            }
        }
    }
}