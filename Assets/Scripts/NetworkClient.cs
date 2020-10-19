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
                //SetupClient(hsMsg);
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
                Debug.Log("Server update message received!");
                UpdateClientInfo(suMsg);
                break;

            case Commands.SPAWNEDPLAYER:
                ServerUpdateMsg SpawnedPlayerInfo = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Spawned Player Data Received!");
                ExistedSpawnedPlayer(SpawnedPlayerInfo);
                break;

            case Commands.NEWPLAYERSPAWNING:
                PlayerUpdateMsg newPlayerSpawnInfo = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
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
        for (int i = 0; i < data.players.Count; i++)
        {
            GameObject ClientPlayer = Instantiate(clientPlayer);
            ClientList[data.players[i].id] = ClientPlayer;
            ClientPlayer.transform.position = data.players[i].cubPos;
            ClientPlayer.GetComponentInChildren<TextMesh>().text = data.players[i].id;
        }
    }

    void SpawningNewPlayer(PlayerUpdateMsg data)
    {
        GameObject ClientPlayer = Instantiate(clientPlayer);
        ClientList[data.player.id] = ClientPlayer;
        ClientPlayer.GetComponentInChildren<TextMesh>().text = data.player.id;
    }

    void DestroyDisconnectedPlayer(DisconnectedPlayerMsg data)
    {
        for(int i = 0; i < data.disconnectedPlayer.Count; i++)
        {
            if(ClientList.ContainsKey(data.disconnectedPlayer[i]))
            {
                Destroy(ClientList[data.disconnectedPlayer[i]]);
                ClientList.Remove(data.disconnectedPlayer[i]);
            }
        }
    }
}