﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Sanguosha.Lobby;

namespace Sanguosha.LobbyServer
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Single, InstanceContextMode = InstanceContextMode.Single)]
    class LobbyServiceImpl : ILobbyService
    {
        Dictionary<int, Room> rooms;
        int roomId;
        Dictionary<Account, Guid> loggedInAccountToGuid;
        Dictionary<Guid, Account> loggedInGuidToAccount;
        Dictionary<IGameClient, Guid> loggedInChannelsToGuid;
        Dictionary<Guid, IGameClient> loggedInGuidToChannel;
        Dictionary<Guid, Room> loggedInGuidToRoom;

        private bool VerifyClient(LoginToken token)
        {
            if (loggedInGuidToAccount.ContainsKey(token.token))
            {
                if (loggedInGuidToChannel.ContainsKey(token.token))
                {
                    if (loggedInGuidToChannel[token.token] == OperationContext.Current.GetCallbackChannel<IGameClient>())
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public LobbyServiceImpl()
        {
            rooms = new Dictionary<int, Room>();
            loggedInGuidToAccount = new Dictionary<Guid, Account>();
            loggedInGuidToChannel = new Dictionary<Guid, IGameClient>();
            loggedInAccountToGuid = new Dictionary<Account, Guid>();
            loggedInChannelsToGuid = new Dictionary<IGameClient, Guid>();
            loggedInGuidToRoom = new Dictionary<Guid, Room>();
            roomId = 1;
        }

        public LoginStatus Login(int version, string username, out LoginToken token)
        {
            Console.WriteLine("{0} logged in", username);
            var connection = OperationContext.Current.GetCallbackChannel<IGameClient>(); 
            token = new LoginToken();
            token.token = System.Guid.NewGuid();
            Account account = new Account() { Username = username };
            loggedInGuidToAccount.Add(token.token, account);
            loggedInGuidToChannel.Add(token.token, connection);
            loggedInAccountToGuid.Add(account, token.token);
            loggedInChannelsToGuid.Add(connection, token.token);
            return LoginStatus.Success;
        }

        public void Logout(LoginToken token)
        {
            if (VerifyClient(token))
            {
                Console.WriteLine("{0} logged out", loggedInGuidToAccount[token.token].Username);
                loggedInGuidToAccount.Remove(token.token);
            }
        }

        public IEnumerable<Room> GetRooms(LoginToken token, bool notReadyRoomsOnly)
        {
            if (VerifyClient(token))
            {
                List<Room> ret = new List<Room>();
                foreach (var pair in rooms)
                {
                    if (!notReadyRoomsOnly || !pair.Value.InProgress)
                    {
                        ret.Add(pair.Value);
                    }
                }
                return ret;
            }
            return null;
        }

        public Room CreateRoom(LoginToken token, string password = null)
        {
            if (VerifyClient(token))
            {
                while (rooms.ContainsKey(roomId))
                {
                    roomId++;
                }
                Room room = new Room();
                for (int i = 0; i < 8; i++)
                {
                    room.Seats.Add(new Seat() { Ready = false, Disabled = false });
                }
                room.Seats[0].Account = loggedInGuidToAccount[token.token];
                room.Id = roomId;
                rooms.Add(roomId, room);
                if (loggedInGuidToRoom.ContainsKey(token.token))
                {
                    loggedInGuidToRoom.Remove(token.token);
                }
                loggedInGuidToRoom.Add(token.token, room);
                Console.WriteLine("created room {0}", roomId);
                return room;
            }
            Console.WriteLine("Invalid createroom call");
            return null;
        }

        public int EnterRoom(LoginToken token, int roomId, bool spectate, string password = null)
        {
            if (VerifyClient(token))
            {
                if (rooms.ContainsKey(roomId))
                {
                        if (rooms[roomId].InProgress) return -1;
                        int seatNo = 1;
                        foreach (var seat in rooms[roomId].Seats)
                        {
                            if (seat.Account == null)
                            {
                                if (loggedInGuidToRoom.ContainsKey(token.token))
                                {
                                    loggedInGuidToRoom.Remove(token.token);
                                }
                                loggedInGuidToRoom.Add(token.token, rooms[roomId]);
                                seat.Account = loggedInGuidToAccount[token.token];
                                foreach (var notify in rooms[roomId].Seats)
                                {
                                    loggedInGuidToChannel[loggedInAccountToGuid[notify.Account]].NotifyRoomUpdate(rooms[roomId].Id, rooms[roomId]);
                                }
                                return seatNo;
                            }
                            seatNo++;
                        }
                        return (int)RoomOperationResult.Full;
                }
            }
            return (int)RoomOperationResult.Auth;
        }

        public bool ExitRoom(LoginToken token, int roomId)
        {
            if (VerifyClient(token))
            {
                if (loggedInGuidToRoom.ContainsKey(token.token))
                {
                    foreach (var seat in loggedInGuidToRoom[token.token].Seats)
                    {
                        if (seat.Account == loggedInGuidToAccount[token.token])
                        {
                            seat.Account = null;
                            foreach (var notify in rooms[roomId].Seats)
                            {
                                loggedInGuidToChannel[loggedInAccountToGuid[notify.Account]].NotifyRoomUpdate(rooms[roomId].Id, rooms[roomId]);
                            }
                            loggedInGuidToRoom.Remove(token.token);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool RoomOperations(RoomOperation op, int arg1, int arg2, out int result)
        {
            throw new NotImplementedException();
        }
    }
}
