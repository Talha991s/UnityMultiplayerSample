using UnityEngine;
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
    // private Dictionary<string, float> clientHeartBeat = new Dictionary<string, float>();

    private int pktID = 0;

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

        InvokeRepeating("SendAllClientStats", 0.1f, 0.008f);
        InvokeRepeating("ChangeColors", 2.0f, 1.0f);
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
        HandshakeMsg d = new HandshakeMsg();
        d.player.id = c.InternalId.ToString();
        Assert.IsTrue(c.IsCreated);
        SendToClient(JsonUtility.ToJson(d), c);

        //send to server now

        ListOfSpawnedPlayer spawnedPlayers = new ListOfSpawnedPlayer();

        foreach(var client in clientList)
        {
            spawnedPlayers.players.Add(client.Value);
        }
        Assert.IsTrue(c.IsCreated);
        SendToClient(JsonUtility.ToJson(spawnedPlayers), c);

        NewPlayerMessage NewPlayer = new NewPlayerMessage();
        NewPlayer.player.id = c.InternalId.ToString();
        
        for(int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(NewPlayer), m_Connections[i]);
        }

        m_Connections.Add(c);
        clientList[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();
        clientList[c.InternalId.ToString()].heartBeat = Time.time;
        //// Example to send a handshake message:
        //HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i)
    {
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("PktID:  "+ pktID+"Handshake receive for " + hsMsg.player.id);
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player "+pktID+"  update message received for : "+puMsg.player.id);
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
        pktID++;
    }

    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
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
                    OnData(stream, i);
                    //clientHeartBeat[m_Connections[i].InternalId.ToString()] = Time.time;
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
        
        HeartBeatCheck();
    }

    void UpdateClientInfo(PlayerUpdateMsg puMsg)
    {
        if (clientList.ContainsKey(puMsg.player.id))
        {
            clientList[puMsg.player.id].id = puMsg.player.id;
            clientList[puMsg.player.id].cubPos = puMsg.player.cubPos;
            clientList[puMsg.player.id].heartBeat = Time.time;
        }
    }


    void SendPlayerInfoToClient()
    {
        ServerUpdateMsg message = new ServerUpdateMsg();

        foreach(var client in clientList)
        {
            message.players.Add(client.Value);
        }

        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(message), m_Connections[i]);
        }
    }


    void HeartBeatCheck()
    {
        List<string> deleteList = new List<string>();
        foreach(var heart in clientList)
        {
            if(Time.time - heart.Value.heartBeat >= 1.0f)
            {
                deleteList.Add(heart.Key);
            }
        }
        if(deleteList.Count > 0)
        {
            for (int i = 0; i < deleteList.Count; i++)
            {
                clientList.Remove(deleteList[i]);
                //clientHeartBeat.Remove(deleteList[i]);
            }
            DisconnectedPlayerMsg disconnectMessage = new DisconnectedPlayerMsg();
            disconnectMessage.disconnectedPlayer = deleteList;

            for(int i = 0; i < m_Connections.Length; i++)
            {
                if(!deleteList.Contains(m_Connections[i].InternalId.ToString()))
                {
                    Assert.IsTrue(m_Connections[i].IsCreated);
                    SendToClient(JsonUtility.ToJson(disconnectMessage), m_Connections[i]);
                }

                
            }
        }
    }


    void ChangeColorOfClient()
    {
        //Debug.Log("Change Color");
        foreach (var player in clientList)
        {
            player.Value.cubeColor = new Color(UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f));

        }
    }


}