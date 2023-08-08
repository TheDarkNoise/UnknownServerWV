﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using NetDefines;

namespace Server
{
    public static class BattleRoyaleMode
    {
        public static string mapName;
        public static string[] spawnLocNames;
        public static int spawnLocIdx;
        public static List<NetMapInfo> mapInfos = new List<NetMapInfo>()
        {
            new NetMapInfo("sanhok",
                            new List<string>()
                            {
                                "Boot Camp"
                            }),
            new NetMapInfo("vikendi",
                            new List<string>()
                            {
                                "Not available yet!"
                            }),
            new NetMapInfo("erangel",
                            new List<string>()
                            {
                                "Not available yet!"
                            }),
            new NetMapInfo("miramar",
                            new List<string>()
                            {
                                "Somewhere"
                            }),
        };
        public static void Start()
        {
            Log.Print("Starting battle royale mode...");
            if (spawnLocIdx == 0)
                spawnLocIdx = NetHelper.rnd.Next(spawnLocNames.Length);
            else
                spawnLocIdx--;
            Log.Print("Choosen spawn location: " + spawnLocNames[spawnLocIdx]);
            Backend.mode = ServerMode.BattleRoyaleMode;
            Backend.modeState = ServerModeState.BR_LobbyState;
            Backend.Start();
            MainServer.Start();
            StatusServer.Start();
            BattleRoyaleServerLogic.Start();
        }
        public static void Stop()
        {
            Log.Print("Stopping battle royale mode...");
            StatusServer.Stop();
            MainServer.Stop();
            Backend.Stop();
            BattleRoyaleServerLogic.Stop();
        }

        public static void HandleMessage(byte[] msg, ClientInfo client)
        {
            uint ID;
            byte[] data;
            uint objectID, index, count, fromID, toID;
            ItemSpawnInfo spawnInfo;
            NetDefines.StateDefines.NetState_Inventory inventory;
            MemoryStream m = new MemoryStream(msg);
            MemoryStream tmp = new MemoryStream();
            BackendCommand cmd = (BackendCommand)NetHelper.ReadU32(m);
            if (!Backend.ShouldFilterInLog(cmd))
                Log.Print("BattleRoyaleMode: Client " + client.ID + " send CMD " + cmd);
            switch (cmd)
            {
                //Requests
                case BackendCommand.WelcomeReq:
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.WelcomeRes, Encoding.UTF8.GetBytes(Config.settings["name"]), client._sync);
                    break;
                case BackendCommand.PingReq:
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.PingRes, new byte[0], client._sync);
                    client.sw.Restart();
                    break;
                case BackendCommand.LoginReq:
                    string key = "";
                    while (m.Position < m.Length)
                        key += (char)m.ReadByte();
                    PlayerProfile target = null;
                    foreach (PlayerProfile p in Config.profiles)
                        if (p.key == key)
                        {
                            target = p;
                            break;
                        }
                    if (target == null)
                    {
                        Log.Print("cant find player profile for client ID=" + client.ID + " with key " + key);
                        NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.LoginFailRes, new byte[0], client._sync);
                    }
                    else
                    {
                        Log.Print("Client ID=" + client.ID + " tries to login as " + target.name);
                        bool found = false;
                        foreach (ClientInfo c in Backend.clientList)
                            if (c.profile != null && c.profile.key == key)
                            {
                                found = true;
                                break;
                            }
                        if (found)
                        {
                            Log.Print("Error : client ID=" + client.ID + " cant login as " + target.name + " because another client is already logged in");
                            NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.LoginFailRes, new byte[0], client._sync);
                        }
                        else
                        {
                            Log.Print("Client ID=" + client.ID + " logged in as " + target.name);
                            client.profile = target;
                            client.teamID = Backend.clientTeamIDCounter++;
                            m = new MemoryStream();
                            NetHelper.WriteU32(m, client.ID);
                            NetHelper.WriteU32(m, client.teamID);
                            NetHelper.WriteU32(m, (uint)target.name.Length);
                            foreach (char c in target.name)
                                m.WriteByte((byte)c);
                            NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.LoginSuccessRes, m.ToArray(), client._sync);
                            Backend.BroadcastCommandExcept((uint)BackendCommand.RefreshPlayerListReq, new byte[0], client);
                        }
                    }
                    break;
                case BackendCommand.GetMapReq:
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetMapRes, Encoding.UTF8.GetBytes(mapName), client._sync);
                    break;
                case BackendCommand.GetSpawnLocReq:
                    NetHelper.WriteU32(tmp, (uint)spawnLocIdx);
                    byte[] spawnLocName = Encoding.UTF8.GetBytes(spawnLocNames[spawnLocIdx]);
                    tmp.Write(spawnLocName, 0, spawnLocName.Length);
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetSpawnLocRes, tmp.ToArray(), client._sync);
                    break;
                case BackendCommand.CreatePlayerObjectReq:
                    NetObjPlayerState playerTransform = new NetObjPlayerState();
                    playerTransform.ID = ObjectManager.objectIDcounter++;
                    playerTransform.accessKey = ObjectManager.MakeNewAccessKey();
                    playerTransform.ReadUpdate(m);
                    ObjectManager.objects.Add(playerTransform);
                    client.objIDs.Add(playerTransform.ID);
                    data = playerTransform.Create(true);
                    m = new MemoryStream();
                    NetHelper.WriteU32(m, (uint)spawnLocIdx);
                    m.Write(data, 0, data.Length);
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.CreatePlayerObjectRes, m.ToArray(), client._sync);
                    data = playerTransform.Create(false);
                    m = new MemoryStream();
                    NetHelper.WriteU32(m, client.ID);
                    m.Write(data, 0, data.Length);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.CreateEnemyObjectReq, m.ToArray(), client);
                    break;
                case BackendCommand.GetAllObjectsReq:
                    m = new MemoryStream();
                    foreach (NetObject no in ObjectManager.objects)
                    {
                        NetHelper.WriteU32(m, no.ID);
                        NetHelper.WriteU32(m, (uint)no.type);
                        tmp = new MemoryStream();
                        no.WriteUpdate(tmp);
                        data = tmp.ToArray();
                        NetHelper.WriteU32(m, (uint)data.Length);
                        m.Write(data, 0, data.Length);
                    }
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetAllObjectsRes, m.ToArray(), client._sync);
                    break;
                case BackendCommand.ReloadTriggeredReq:
                    objectID = NetHelper.ReadU32(m);
                    uint len = NetHelper.ReadU32(m);
                    string name = "";
                    for (int i = 0; i < len; i++)
                        name += (char)m.ReadByte();
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.ReloadTriggeredReq, data, client);
                    break;
                case BackendCommand.InventoryUpdateReq:
                    objectID = NetHelper.ReadU32(m);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.InventoryUpdateReq, data, client);
                    break;
                case BackendCommand.ShotTriggeredReq:
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.ShotTriggeredReq, data, client);
                    break;
                case BackendCommand.ImpactTriggeredReq:
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.ImpactTriggeredReq, data, client);
                    break;
                case BackendCommand.DoorStateChangedReq:
                    float[] pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    int newState = (int)NetHelper.ReadU32(m);
                    DoorManager.UpdateDoor(pos, newState);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.DoorStateChangedReq, data, client);
                    break;
                case BackendCommand.GetDoorStatesReq:
                    m = new MemoryStream();
                    NetHelper.WriteU32(m, (uint)DoorManager.doorChanges.Count);
                    foreach (DoorManager.DoorInfo di in DoorManager.doorChanges)
                    {
                        foreach (float f in di.location)
                            NetHelper.WriteFloat(m, f);
                        NetHelper.WriteU32(m, (uint)di.state);
                    }
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetDoorStatesRes, m.ToArray(), client._sync);
                    break;
                case BackendCommand.PlayerReadyReq:
                    lock (client._sync)
                    {
                        client.isReady = true;
                    }
                    break;
                case BackendCommand.PlayerNotReadyReq:
                    lock (client._sync)
                    {
                        client.isReady = false;
                    }
                    break;
                case BackendCommand.PlayerHitReq:
                    ID = NetHelper.ReadU32(m);
                    HitLocation loc = (HitLocation)NetHelper.ReadU32(m);
                    foreach (ClientInfo other in Backend.clientList)
                    {
                        if (other.objIDs.Contains(ID))
                        {
                            m = new MemoryStream();
                            NetHelper.WriteU32(m, (uint)loc);
                            NetHelper.WriteU32(m, (uint)client.ID);
                            NetHelper.ServerSendCMDPacket(other.ns, (uint)BackendCommand.PlayerHitReq, m.ToArray(), other._sync);
                            break;
                        }
                    }
                    break;
                case BackendCommand.PlayerDiedReq:
                    ID = NetHelper.ReadU32(m);
                    Log.Print("Player with id 0x" + ID.ToString("X8") + " died!");
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    inventory = new NetDefines.StateDefines.NetState_Inventory();
                    inventory.Read(m);
                    count = NetHelper.ReadU32(m);
                    name = "";
                    for (int i = 0; i < count; i++)
                        name = name + (char)m.ReadByte();
                    SpawnManager.RegisterItemContainer(pos, ItemContainerType.PlayerCrate, name, inventory);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommand((uint)BackendCommand.PlayerDiedReq, data);
                    ObjectManager.RemoveClientObjects(client);
                    break;
                case BackendCommand.SpawnGroupItemReq:
                    HandleSpawnGroupItemRequest(client, m);
                    break;
                case BackendCommand.SpawnGroupItemRemoveReq:
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    index = NetHelper.ReadU32(m);
                    SpawnManager.AddSpawnGroupRemoval(pos, (int)index);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommand((uint)BackendCommand.SpawnGroupItemRemoveReq, data);
                    break;
                case BackendCommand.ItemDroppedReq:
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    spawnInfo = new ItemSpawnInfo(m);
                    SpawnManager.droppedItems.Add(new DroppedItemInfo(pos, spawnInfo));
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommand((uint)BackendCommand.ItemDroppedReq, data);
                    break;
                case BackendCommand.RemoveDroppedItemReq:
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    SpawnManager.RemoveDroppedItem(pos);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommand((uint)BackendCommand.RemoveDroppedItemReq, data);
                    break;
                case BackendCommand.RemoveContainerItemReq:
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    index = NetHelper.ReadU32(m);
                    SpawnManager.AddItemContainerRemoval(pos, (int)index);
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommand((uint)BackendCommand.RemoveContainerItemReq, data);
                    break;
                case BackendCommand.SpawnGroupRemovalsReq:
                    pos = new float[] { NetHelper.ReadFloat(m), NetHelper.ReadFloat(m), NetHelper.ReadFloat(m) };
                    m = new MemoryStream();
                    SpawnManager.WriteSpawnGroupRemovals(m, pos);
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.SpawnGroupRemovalsReq, m.ToArray(), client._sync);
                    break;
                case BackendCommand.GetAllPickupsReq:
                    m = new MemoryStream();
                    SpawnManager.WriteDroppedItems(m);
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetAllPickupsReq, m.ToArray(), client._sync);
                    break;
                case BackendCommand.GetAllItemContainersReq:
                    m = new MemoryStream();
                    SpawnManager.WriteItemContainers(m);
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetAllItemContainersReq, m.ToArray(), client._sync);
                    break;
                case BackendCommand.PlayFootStepSoundReq:
                    data = NetHelper.CopyCommandData(m);
                    Backend.BroadcastCommandExcept((uint)BackendCommand.PlayFootStepSoundReq, data, client);
                    break;
                case BackendCommand.GetPlayersOnServerReq:
                    m = new MemoryStream();
                    NetHelper.WriteU32(m, (uint)Backend.clientList.Count);
                    foreach (ClientInfo other in Backend.clientList)
                    {
                        NetHelper.WriteU32(m, other.ID);
                        NetHelper.WriteU32(m, other.teamID);
                        m.WriteByte((byte)(other.isTeamReady ? 1 : 0));
                        NetHelper.WriteU32(m, (uint)other.profile.name.Length);
                        foreach (char c in other.profile.name)
                            m.WriteByte((byte)c);
                    }
                    NetHelper.ServerSendCMDPacket(client.ns, (uint)BackendCommand.GetPlayersOnServerRes, m.ToArray(), client._sync);
                    break;
                case BackendCommand.TeamInviteReq:
                    fromID = NetHelper.ReadU32(m);
                    toID = NetHelper.ReadU32(m);
                    foreach (ClientInfo other in Backend.clientList)
                        if (other.ID == toID)
                        {
                            Log.Print("Sending team invite from " + fromID + " to " + toID);
                            data = NetHelper.CopyCommandData(m);
                            NetHelper.ServerSendCMDPacket(other.ns, (uint)BackendCommand.TeamInviteReq, data, other._sync);
                            break;
                        }
                    break;
                case BackendCommand.TeamInviteAcceptReq:
                    toID = NetHelper.ReadU32(m);
                    foreach (ClientInfo other in Backend.clientList)
                        if (other.ID == toID)
                        {
                            Log.Print("Accepting team invite by " + client.ID + " into team of " + toID);
                            client.teamID = other.teamID;
                            Backend.BroadcastCommand((uint)BackendCommand.RefreshPlayerListReq, new byte[0]);
                            break;
                        }
                    break;
                case BackendCommand.TeamLeaveReq:
                    client.teamID = Backend.clientTeamIDCounter++;
                    Backend.BroadcastCommand((uint)BackendCommand.RefreshPlayerListReq, new byte[0]);
                    break;
                case BackendCommand.SetTeamReadyStateReq:
                    client.isTeamReady = m.ReadByte() == 1;
                    Backend.BroadcastCommand((uint)BackendCommand.RefreshPlayerListReq, new byte[0]);
                    break;
                //Responses
                case BackendCommand.DeleteObjectsRes:
                case BackendCommand.CreateEnemyObjectRes:
                case BackendCommand.GetAllObjectsRes:
                    break;
                default:
                    throw new Exception("Unknown command 0x" + cmd.ToString("X"));
            }
        }

        

        private static void HandleSpawnGroupItemRequest(ClientInfo client, Stream s)
        {
            float[] pos = new float[] { NetHelper.ReadFloat(s), NetHelper.ReadFloat(s), NetHelper.ReadFloat(s) };
            uint count = NetHelper.ReadU32(s);
            SpawnTierLevel tierLevel = (SpawnTierLevel)NetHelper.ReadU32(s);
            List<ItemSpawnInfo> resultList = SpawnManager.GetRandomSpawn(tierLevel, count);
            MemoryStream m = new MemoryStream();
            foreach (float f in pos)
                NetHelper.WriteFloat(m, f);
            NetHelper.WriteU32(m, (uint)resultList.Count);
            foreach (ItemSpawnInfo i in resultList)
                i.Write(m);
            Backend.BroadcastCommand((uint)BackendCommand.SpawnGroupItemReq, m.ToArray());
        }
    }
}
