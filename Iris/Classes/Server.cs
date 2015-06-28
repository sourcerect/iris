using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Iris.Server
{
    class Server
    {
        const int MS_BETWEEN = 16;
        const int QUIT_DELAY = 100;
        private NetServer server;
        private bool quit = false;

        //local data
        List<Player> dActors = new List<Player>();

        public Server()
        {
            NetPeerConfiguration cfg = new NetPeerConfiguration("bandit");
            cfg.Port = 5635;
            server = new NetServer(cfg);
            server.RegisterReceivedCallback(new SendOrPostCallback(GotLidgrenMessage), new SynchronizationContext());
        }

        public void Start()
        {
            server.Start();

            //block until quit
            while (!quit)
            {
                Thread.Sleep(MS_BETWEEN);
            }

            Console.WriteLine("Exiting");
        }

        public void GotLidgrenMessage(object peer)
        {
            NetIncomingMessage msg;
            while ((msg = server.ReadMessage()) != null)
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.VerboseDebugMessage:
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        Console.WriteLine(msg.ReadString());
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        var newStatus = (NetConnectionStatus)msg.ReadByte();
                        if (newStatus == NetConnectionStatus.Connected)
                        {
                            OnConnect(msg);
                        }
                        else if (newStatus == NetConnectionStatus.Disconnected)
                        {
                            OnDisconnect(msg);
                        }
                        break;
                    case NetIncomingMessageType.Data:
                        HandleGameMessage(msg);
                        break;
                    default:
                        Console.WriteLine(string.Format("Unhandled type: {0}", msg.MessageType));
                        break;
                }
                server.Recycle(msg);
            }
        }

        private void HandleGameMessage(NetIncomingMessage msg)
        {
            string type = msg.ReadString();

            switch (type)
            {
                case "POS":
                    HandlePOS(msg);
                    break;
                case "LIFE":
                    HandleLIFE(msg);
                    break;
                case "NAME":
                    HandleNAME(msg);
                    break;
                case "BULLET":
                    HandleBULLET(msg);
                    break;
                default:
                    Console.WriteLine(string.Format("Bad message type {0} from player {1}",
                        type, msg.SenderConnection.RemoteUniqueIdentifier));
                    break;
            }
        }

        private void HandleBULLET(NetIncomingMessage msg)
        {
            long owner = msg.SenderConnection.RemoteUniqueIdentifier;
            float x = msg.ReadFloat();
            float y = msg.ReadFloat();
            float dx = msg.ReadFloat();
            float dy = msg.ReadFloat();

            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write(owner);
            outMsg.Write(x);
            outMsg.Write(y);
            outMsg.Write(dx);
            outMsg.Write(dy);
            server.SendToAll(outMsg, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        }

        private void HandleNAME(NetIncomingMessage msg)
        {
            string newName = msg.ReadString();
            string oldName = GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Name;
            Console.WriteLine(string.Format("NAME: {0} changed {1}", oldName, newName));

            //save name in dict
            GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Name = newName;

            //inform ALL clients about his name change
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("NAME");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsg.Write(newName);
            server.SendToAll(outMsg, null, NetDeliveryMethod.ReliableOrdered, 0);
        }

        private void HandleLIFE(NetIncomingMessage msg)
        {
            int newHp = msg.ReadInt32();
            Console.WriteLine(string.Format("LIFE: {0}: {1}", msg.SenderConnection.RemoteUniqueIdentifier, newHp));

            //save value
            GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Life = newHp;

            //inform ALL clients about his pining for the fjords
            NetOutgoingMessage outMsgLife = server.CreateMessage();
            outMsgLife.Write("LIFE");
            outMsgLife.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsgLife.Write(newHp);
            server.SendToAll(outMsgLife, null, NetDeliveryMethod.ReliableOrdered, 0);
        }

        private void HandlePOS(NetIncomingMessage msg)
        {
            float newX = msg.ReadFloat();
            float newY = msg.ReadFloat();

            //inform ALL clients about position change
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("POS");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsg.Write(newX);
            outMsg.Write(newY);
            server.SendToAll(outMsg, null, NetDeliveryMethod.ReliableUnordered, 0);
        }

        private Player GetPlayerFromUID(long remoteUniqueIdentifier)
        {
            return dActors.FirstOrDefault(p => p.UID == remoteUniqueIdentifier);
        }

        private void OnDisconnect(NetIncomingMessage msg)
        {
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("PART");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);

            server.SendToAll(outMsg, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            Console.WriteLine(string.Format("PART: {0}", msg.SenderConnection.RemoteUniqueIdentifier));

            //remove datas
            dActors.Remove((Player)msg.SenderConnection.Tag);
        }

        private void OnConnect(NetIncomingMessage msg)
        {
            //tell everyone else he joined
            {
                NetOutgoingMessage outMsg = server.CreateMessage();
                outMsg.Write("JOIN");
                outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
                server.SendToAll(outMsg, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            }

            Console.WriteLine(string.Format("JOIN: {0}", msg.SenderConnection.RemoteUniqueIdentifier));

            InformNewbieState(msg);

            //intial data finished sending; add him to the player list, tag his Player for easy access
            Player thisPlayer = new Player();
            thisPlayer.UID = msg.SenderConnection.RemoteUniqueIdentifier;
            dActors.Add(thisPlayer);
            msg.SenderConnection.Tag = thisPlayer;
        }

        private void InformNewbieState(NetIncomingMessage msg)
        {
            NetOutgoingMessage newbieState = server.CreateMessage();

            newbieState.Write("MULTI_ON");

            foreach (var actor in dActors) //not using server.Connections
            {
                Player plr = (Player)actor;
                newbieState.Write("JOIN");
                newbieState.Write(plr.UID);

                newbieState.Write("NAME");
                newbieState.Write(plr.UID); //long uid
                newbieState.Write(plr.Name); //string name

                newbieState.Write("LIFE");
                newbieState.Write(plr.UID);
                newbieState.Write(plr.Life);
            }

            newbieState.Write("MULTI_OFF");

            server.SendMessage(newbieState, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered);
        }
    }

    public class Player
    {
        public long UID { get; set; }
        public string Name { get; set; }
        public int Life { get; set; }
    }
}
