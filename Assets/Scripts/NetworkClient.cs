using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    private Dictionary<string, GameObject> ClientUList = new Dictionary<string, GameObject>();
    public string CclientID;
    public GameObject playerIngame;
    public GameObject playerpref;
    PlayerUpdateMsg playerIngameUpdatemessage = new PlayerUpdateMsg();
    
    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);
    }
    
    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");

        InvokeRepeating("PlayerInfo", 0.1f, 0.03f);
        //// Example to send a handshake message:
        HandshakeMsg m = new HandshakeMsg();
        m.player.id = m_Connection.InternalId.ToString();
        SendToServer(JsonUtility.ToJson(m));
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received! Player ID : ");
            SetupClient(hsMsg);
            break;

            //case Commands.PLAYER_ID:
            //    PlayerUpdateMsg iDee = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            //    Debug.Log("Got ID From Sever!");
            //    playerInfo.player.id = iDee.player.id;
            //    playerIDtext.text = playerInfo.player.id;
            //    break;

            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Received Player Position fron Server : "); // + puMsg.player.cubPos);
                break;

            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);

                AllPlayerUpdates(suMsg);
                Debug.Log("Server update message received!");
                break;

            case Commands.SPAWNEDPLAYER:
                ListOfSpawnedPlayer SpawnedPlayerInfo = JsonUtility.FromJson<ListOfSpawnedPlayer>(recMsg);

                ExistedSpawnedPlayer(SpawnedPlayerInfo);
                Debug.Log("Spawned Player Data Received!");
                break;

            case Commands.NEWPLAYERSPAWNING:
                NewPlayerMessage newPlayerSpawnInfo = JsonUtility.FromJson<NewPlayerMessage>(recMsg);
                Debug.Log("New Client Data received");
                SpawningNewPlayer(newPlayerSpawnInfo);
                break;

            case Commands.DISCONNECTPLAYER:
                DisconnectedPlayerMsg DisconnectPlayer = JsonUtility.FromJson<DisconnectedPlayerMsg>(recMsg);
                Debug.Log("Player Disconnected");
                DestroyDisconnectedPlayer(DisconnectPlayer);
                break;

            default:
            Debug.Log("Unrecognized message received!");
            break;
        }
    }

    void Disconnect(){
        //Debug.Log("DISCONNECTED FROM SERVER! ");
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }   
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }

    void HeartBeat()
    {
        playerIngameUpdatemessage.player.cubPos = playerIngame.transform.position;
        SendToServer(JsonUtility.ToJson(playerIngameUpdatemessage));
    }

    void SetupClient(HandshakeMsg hsMsg)
    {
        playerIngame = Instantiate(playerpref);
        playerIngame.GetComponent<PlayerController>().clientControlled = true;
        playerIngame.GetComponent<PlayerController>().networkClient = this;
        playerIngameUpdatemessage.player.id = hsMsg.player.id;
        CclientID = hsMsg.player.id;
    }

    void AllPlayerUpdates(ServerUpdateMsg serverUpdatemessage)
    {
        for(int i = 0; i< serverUpdatemessage.players.Count; i++)
        {
            if(ClientUList.ContainsKey(serverUpdatemessage.players[i].id))
            {
                ClientUList[serverUpdatemessage.players[i].id].transform.position = serverUpdatemessage.players[i].cubPos;
                ClientUList[serverUpdatemessage.players[i].id].GetComponent<Renderer>().material.color = serverUpdatemessage.players[i].cubeColor;

            }
            else if(playerIngameUpdatemessage.player.id == serverUpdatemessage.players[i].id)
            {
                playerIngame.gameObject.GetComponent<Renderer>().material.color = serverUpdatemessage.players[i].cubeColor;
                playerIngameUpdatemessage.player.cubeColor = serverUpdatemessage.players[i].cubeColor;
            }
        }
    }

    void ExistedSpawnedPlayer(ListOfSpawnedPlayer spawnedmessage)
    {
        for (int i = 0; i < spawnedmessage.players.Count; i++)
        {
            GameObject ClientPlayer = Instantiate(playerpref);
            ClientUList[spawnedmessage.players[i].id] = ClientPlayer;
            ClientPlayer.transform.position = spawnedmessage.players[i].cubPos;
            ClientPlayer.GetComponent<PlayerController>().clientControlled = false;
            ClientPlayer.GetComponent<PlayerController>().networkClient = this;
        }
    }

    void SpawningNewPlayer(NewPlayerMessage messagefornewplayer)
    {
        GameObject ClientPlayer = Instantiate(playerpref);
        ClientUList[messagefornewplayer.player.id] = ClientPlayer;
        ClientPlayer.GetComponent<PlayerController>().clientControlled = false;
        ClientPlayer.GetComponent<PlayerController>().networkClient = this;
    }

    void DestroyDisconnectedPlayer(DisconnectedPlayerMsg disconnectedlist)
    {
        foreach(var playerID in disconnectedlist.disconnectedPlayer)
        {
            if(ClientUList.ContainsKey(playerID))
            {
                Destroy(ClientUList[playerID]);
                ClientUList.Remove(playerID);
            }
        }
    }
}