﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;

using Sanguosha.Core.Cards;
using Sanguosha.Core.Players;
using Sanguosha.Core.Triggers;
using Sanguosha.Core.Exceptions;
using Sanguosha.Core.UI;
using Sanguosha.Core.Skills;


namespace Sanguosha.Core.Games
{
    
    public class GameOverException : SgsException { }

    public struct CardsMovement
    {
        public List<Card> cards;
        public DeckPlace to;
    }

    public enum DamageElement
    {
        None,
        Fire,
        Lightning,
    }

    public enum DiscardReason
    {
        Discard,
        Play,
        Use,
        Judge,
    }

    public abstract class Game : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Create the OnPropertyChanged method to raise the event 
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        
        class EndOfDealingDeckException : SgsException { }

        
        class GameAlreadyStartedException : SgsException { }

        public GameOptions Options { get; protected set; }

        static Game()
        {
            games = new Dictionary<Thread,Game>();
        }

        List<CardHandler> availableCards;

        public List<CardHandler> AvailableCards
        {
            get { return availableCards; }
        }


        List<DelayedTriggerRegistration> triggersToRegister;

        private Dictionary<Player, List<Player>> handCardVisibility;

        public Dictionary<Player, List<Player>> HandCardVisibility
        {
            get { return handCardVisibility; }
            set { handCardVisibility = value; }
        }

        public Game()
        {
            cardSet = new List<Card>();
            originalCardSet = new List<Card>();
            triggers = new Dictionary<GameEvent, List<Trigger>>();
            decks = new DeckContainer();
            players = new List<Player>();
            cardHandlers = new Dictionary<string, CardHandler>();
            uiProxies = new Dictionary<Player, IUiProxy>();
            currentActingPlayer = null;
            triggersToRegister = new List<DelayedTriggerRegistration>();
            isDying = new Stack<Player>();
            handCardVisibility = new Dictionary<Player, List<Player>>();
            Options = new GameOptions() { CheatingEnabled = true };
        }

        public void LoadExpansion(Expansion expansion)
        {
            OriginalCardSet.AddRange(expansion.CardSet);

            if (expansion.TriggerRegistration != null)
            {
                triggersToRegister.AddRange(expansion.TriggerRegistration);
            }
        }

        public Network.Server GameServer { get; set; }
        public Network.Client GameClient { get; set; }


        public void SyncUnknownLocationCard(Player player, Card card)
        {
            if (GameClient != null)
            {
                bool confirmed = true;
                Game.CurrentGame.SyncConfirmationStatus(ref confirmed);
                if (confirmed)
                {
                    if (player.Id != GameClient.SelfId)
                    {
                        return;
                    }
                    GameClient.Receive();
                }
            }
            else if (GameServer != null)
            {
                bool status = true;
                if (card.Place.DeckType == DeckType.Equipment || card.Place.DeckType == DeckType.DelayedTools)
                {
                    status = false;
                }
                Game.CurrentGame.SyncConfirmationStatus(ref status);
                if (status)
                {
                    card.RevealOnce = true;
                    GameServer.SendObject(player.Id, card);
                }
            }
        }
        public void SyncUnknownLocationCardAll(Card card)
        {
            foreach (Player p in players)
            {
                SyncUnknownLocationCard(p, card);
            }
        }

        public void SyncCard(Player player, ref Card card)
        {
            if (card.Place.DeckType == DeckType.Equipment || card.Place.DeckType == DeckType.DelayedTools)
            {
                return;
            }
            if (GameClient != null)
            {
                if (player.Id != GameClient.SelfId)
                {
                    return;
                }
                card = GameClient.Receive() as Card;
                Trace.Assert(card != null);
            }
            else if (GameServer != null)
            {
                card.RevealOnce = true;
                GameServer.SendObject(player.Id, card);
            }
        }

        public void SyncCards(Player player, List<Card> cards)
        {
            int i;
            for (i = 0; i < cards.Count; i++)
            {
                var temp = cards[i];
                SyncCard(player, ref temp);
                cards[i] = temp;
            }
        }

        public void SyncCardAll(ref Card card)
        {
            foreach (Player p in players)
            {
                SyncCard(p, ref card);
            }
        }

        public void SyncCardsAll(List<Card> cards)
        {
            foreach (Player p in players)
            {
                SyncCards(p, cards);
            }
        }

        public void SyncImmutableCard(Player player, Card card)
        {
            if (card.Place.DeckType == DeckType.Equipment || card.Place.DeckType == DeckType.DelayedTools)
            {
                return;
            }
            if (GameClient != null)
            {
                if (player.Id != GameClient.SelfId)
                {
                    return;
                }
                GameClient.Receive();
            }
            else if (GameServer != null)
            {
                card.RevealOnce = true;
                GameServer.SendObject(player.Id, card);
            }
        }

        public void SyncImmutableCards(Player player, List<Card> cards)
        {
            foreach (var card in cards)
            {
                SyncImmutableCard(player, card);
            }
        }

        public void SyncImmutableCardAll(Card card)
        {
            foreach (Player p in players)
            {
                SyncImmutableCard(p, card);
            }
        }

        public void SyncImmutableCardsAll(List<Card> cards)
        {
            foreach (Player p in players)
            {
                SyncImmutableCards(p, cards);
            }
        }

        public void SyncConfirmationStatus(ref bool confirmed)
        {
            if (GameServer != null)
            {
                for (int i = 0; i < GameServer.MaxClients; i++)
                {
                    GameServer.SendObject(i, confirmed ? 1 : 0);
                }
            }
            else if (GameClient != null)
            {
                object o = GameClient.Receive();
                Trace.Assert(o is int);
                if ((int)o == 1)
                {
                    confirmed = true;
                }
                else
                {
                    confirmed = false;
                }
            }
        }

        public bool IsClient 
        {
            get
            {
                return GameClient != null;
            }
        }
        
        public virtual void Run()
        {
            if (games.ContainsKey(Thread.CurrentThread))
            {
                throw new GameAlreadyStartedException();
            }
            else
            {
                games.Add(Thread.CurrentThread, this);
            }
            if (GameServer != null)
            {
                GameServer.Ready();
            }

            availableCards = new List<CardHandler>();
            foreach (Card c in OriginalCardSet)
            {
                bool typeCheck = false;
                foreach (var type in availableCards)
                {
                    if (type.GetType().Name.Equals(c.Type.GetType().Name))
                    {
                        typeCheck = true;
                        break;
                    }
                }
                if (!typeCheck)
                {
                    availableCards.Add(c.Type);
                }
            }

            foreach (var card in OriginalCardSet)
            {
                //you are client. everything is unknown
                if (IsClient)
                {
                    unknownCard = new Card();
                    unknownCard.Id = Card.UnknownCardId;
                    unknownCard.Rank = 0;
                    unknownCard.Suit = SuitType.None;
                    if (card.Type is Heroes.HeroCardHandler)
                    {
                        unknownCard.Type = new Heroes.UnknownHeroCardHandler();
                    }
                    else
                    {
                        unknownCard.Type = new UnknownCardHandler();
                    }
                }
                //you are server.
                else
                {
                    unknownCard = new Card();
                    unknownCard.CopyFrom(card);
                }
                cardSet.Add(unknownCard);
            }

            foreach (var trig in triggersToRegister)
            {
                RegisterTrigger(trig.key, trig.trigger);
            }

            InitTriggers();
            try
            {
                Emit(GameEvent.GameStart, new GameEventArgs());
            }
            catch (GameOverException)
            {
                Trace.TraceError("Game is over");
            }
#if RELEASE
            catch (Exception e)
            {
                Trace.TraceError(e.StackTrace);
            }
#endif
            Trace.TraceError("Game exited normally");
        }

        /// <summary>
        /// Initialize triggers at game start time.
        /// </summary>
        protected abstract void InitTriggers();

        public static Game CurrentGame
        {
            get { return games[Thread.CurrentThread]; }
            set 
            {
                games[Thread.CurrentThread] = value;                 
            }
        }

        /// <summary>
        /// Mapping from a thread to the game it hosts.
        /// </summary>
        static Dictionary<Thread, Game> games;

        public void RegisterCurrentThread()
        {
            games.Add(Thread.CurrentThread, this);
        }

        List<Card> originalCardSet;

        /// <summary>
        /// All eligible card copied verbatim from the game engine. All cards in this set are known cards.
        /// </summary>
        public List<Card> OriginalCardSet
        {
            get { return originalCardSet; }
        }

        List<Card> cardSet;

        /// <summary>
        /// Current state of all cards used in the game. Some of the cards can be unknown in the client side.
        /// The collection is empty before Run() is called.
        /// </summary>
        public List<Card> CardSet
        {
            get { return cardSet; }
        }

        Card unknownCard;
        Dictionary<GameEvent, List<Trigger>> triggers;

        public void RegisterTrigger(GameEvent gameEvent, Trigger trigger)
        {
            if (trigger == null)
            {
                return;
            }
            if (!triggers.ContainsKey(gameEvent))
            {                
                triggers[gameEvent] = new List<Trigger>();
            }
            triggers[gameEvent].Add(trigger);
        }

        public void UnregisterTrigger(GameEvent gameEvent, Trigger trigger)
        {
            if (trigger == null)
            {
                return;
            }
            if (triggers.ContainsKey(gameEvent))
            {
                Trace.Assert(triggers[gameEvent].Contains(trigger));
                triggers[gameEvent].Remove(trigger);
            }
        }

        private void EmitTriggers(GameEvent e, List<TriggerWithParam> triggers)
        {
            triggers.Sort((a, b) =>
            {
                int result2 = a.trigger.Type.CompareTo(b.trigger.Type);
                if (result2 != 0)
                {
                    return -result2;
                }
                int result = a.trigger.Priority.CompareTo(b.trigger.Priority);
                if (result != 0)
                {
                    return -result;
                }
                Player p = CurrentPlayer;
                int result3 = 0;
                if (a.trigger.Owner != b.trigger.Owner)
                {
                    do
                    {
                        if (p == a.trigger.Owner)
                        {
                            result3 = -1;
                            break;
                        }
                        if (p == b.trigger.Owner)
                        {
                            result3 = 1;
                            break;
                        }
                        p = NextAlivePlayer(p);
                    } while (p != CurrentPlayer);

                }
                return result3;
            });
            foreach (var t in triggers)
            {
                if (t.trigger.Owner == null || !t.trigger.Owner.IsDead)
                {
                    t.trigger.Run(e, t.args);
                }
            }
        }


        /// <summary>
        /// Emit a game event to invoke associated triggers.
        /// </summary>
        /// <param name="gameEvent">Game event to be emitted.</param>
        /// <param name="eventParam">Additional helper for triggers listening on this game event.</param>
        public void Emit(GameEvent gameEvent, GameEventArgs eventParam, bool beforeMove = false)
        {
            if (!this.triggers.ContainsKey(gameEvent)) return;
            List<Trigger> triggers = new List<Trigger>(this.triggers[gameEvent]);
            if (triggers == null) return;
            List<TriggerWithParam> triggersToRun = new List<TriggerWithParam>();
            foreach (var t in triggers)
            {
                if (t.Enabled)
                {
                    triggersToRun.Add(new TriggerWithParam() { trigger = t, args = eventParam });
                }
            }
            if (!atomic)
            {
                EmitTriggers(gameEvent, triggersToRun);
            }
            else
            {
                var triggerPlace = atomicTriggers;
                if (beforeMove)
                {
                    triggerPlace = atomicTriggersBeforeMove;
                }
                if (!triggerPlace.ContainsKey(gameEvent))
                {
                    triggerPlace.Add(gameEvent, new List<TriggerWithParam>());
                }
                triggerPlace[gameEvent].AddRange(triggersToRun);

            }
        }

        private Dictionary<Player, IUiProxy> uiProxies;

        public Dictionary<Player, IUiProxy> UiProxies
        {
            get { return uiProxies; }
            set { uiProxies = value; }
        }

        Dictionary<string, CardHandler> cardHandlers;

        public IGlobalUiProxy GlobalProxy { get; set; }

        public INotificationProxy NotificationProxy { get; set; }

        /// <summary>
        /// Card usage handler for a given card's type name.
        /// </summary>
        public Dictionary<string, CardHandler> CardHandlers
        {
            get { return cardHandlers; }
            set { cardHandlers = value; }
        }

        DeckContainer decks;

        public DeckContainer Decks
        {
            get { return decks; }
            set { decks = value; }
        }

        List<Player> players;

        public List<Player> Players
        {
            get { return players; }
            set { players = value; }
        }

        public List<Player> AlivePlayers
        {
            get
            {
                var list = new List<Player>();
                foreach (Player p in players)
                {
                    if (!p.IsDead)
                    {
                        list.Add(p);
                    }
                }
                return list;
            }
        }

        private bool atomic = false;
        private int atomicLevel = 0;

        private struct TriggerWithParam
        {
            public Trigger trigger;
            public GameEventArgs args;
        }

        List<CardsMovement> atomicMoves;
        Dictionary<GameEvent, List<TriggerWithParam>> atomicTriggers;
        Dictionary<GameEvent, List<TriggerWithParam>> atomicTriggersBeforeMove;
        List<MovementHelper> atomicLogs;
        
        public void EnterAtomicContext()
        {
            atomic = true;
            if (atomicLevel == 0)
            {
                atomicMoves = new List<CardsMovement>();
                atomicTriggers = new Dictionary<GameEvent, List<TriggerWithParam>>();
                atomicLogs = new List<MovementHelper>();
                atomicTriggersBeforeMove = new Dictionary<GameEvent, List<TriggerWithParam>>();
            }
            atomicLevel++;
        }

        public void ExitAtomicContext()
        {
            atomicLevel--;
            if (atomicLevel > 0)
            {
                return;
            }
            var moves = atomicMoves;
            var triggers = atomicTriggers;
            atomic = false;
            foreach (var v in atomicTriggersBeforeMove)
            {
                EmitTriggers(v.Key, v.Value);
            }
            MoveCards(atomicMoves, atomicLogs);
            foreach (var v in atomicTriggers)
            {
                EmitTriggers(v.Key, v.Value);
            }
        }

        private void AddAtomicMoves(List<CardsMovement> moves, List<MovementHelper> logs)
        {
            int i = 0;
            foreach (var m in moves)
            {
                CardsMovement newM = new CardsMovement();
                newM.cards = m.cards;
                newM.to = new DeckPlace(m.to.Player, m.to.DeckType);
                atomicMoves.Add(newM);
                if (logs != null)
                {
                    atomicLogs.Add(logs[i]);
                }
                else
                {
                    atomicLogs.Add(null);
                }
                i++;
            }
        }

        ///<remarks>
        ///YOU ARE NOT ALLOWED TO TRIGGER ANY EVENT ANYWHERE INSIDE THIS FUNCTION!!!!!
        ///你不可以在这个函数中触发任何事件!!!!!
        ///</remarks>
        public void MoveCards(List<CardsMovement> moves, List<MovementHelper> logs, List<bool> insertBefore = null)
        {
            if (atomic)
            {
                AddAtomicMoves(moves, logs);
                return;
            }
            foreach (CardsMovement move in moves)
            {
                List<Card> cards = new List<Card>(move.cards);
                foreach (Card card in cards)
                {
                    if (move.to.Player == null && move.to.DeckType == DeckType.Discard)
                    {
                        SyncImmutableCardAll(card);
                    }
                    if (card.Place.Player != null && move.to.Player != null && move.to.DeckType == DeckType.Hand)
                    {
                        SyncImmutableCard(move.to.Player, card);
                    }
                }
            }

            NotificationProxy.NotifyCardMovement(moves, logs);
            Thread.Sleep(100);

            int i = 0;
            foreach (CardsMovement move in moves)
            {
                List<Card> cards = new List<Card>(move.cards);
                // Update card's deck mapping
                foreach (Card card in cards)
                {
                    Trace.TraceInformation("Card {0}{1}{2} from {3}{4} to {5}{6}.", card.Suit, card.Rank, card.Type.CardType.ToString(),
                        card.Place.Player == null ? "G" : card.Place.Player.Id.ToString(), card.Place.DeckType.Name, move.to.Player == null ? "G" : move.to.Player.Id.ToString(), move.to.DeckType.Name);
                    // unregister triggers for equipment 例如武圣将红色的雌雄双绝（假设有这么一个雌雄双绝）打出杀女性角色，不能发动雌雄
                    if (card.Place.Player != null && card.Place.DeckType == DeckType.Equipment && CardCategoryManager.IsCardCategory(card.Type.Category, CardCategory.Equipment))
                    {
                        Equipment e = card.Type as Equipment;
                        e.UnregisterTriggers(card.Place.Player);
                    }
                    if (move.to.Player != null && move.to.DeckType == DeckType.Equipment && CardCategoryManager.IsCardCategory(card.Type.Category, CardCategory.Equipment))
                    {
                        Equipment e = card.Type as Equipment;
                        e.RegisterTriggers(move.to.Player);
                    }
                    decks[card.Place].Remove(card);
                    if (insertBefore != null && insertBefore[i])
                    {
                        decks[move.to].Insert(0, card);
                    }
                    else
                    {
                        decks[move.to].Add(card);
                    }
                    card.HistoryPlace1 = card.Place;
                    card.Place = move.to;
                    //reset card type if entering hand or discard
                    if (!IsClient && (move.to.DeckType == DeckType.Discard || move.to.DeckType == DeckType.Hand))
                    {
                        card.Log = new ActionLog();
                        card.Type = GameEngine.CardSet[card.Id].Type;
                        card.Suit = GameEngine.CardSet[card.Id].Suit;
                        if (card.Attributes != null) card.Attributes.Clear();
                    }

                    //reset color if entering delayedtools
                    if (move.to.DeckType == DeckType.DelayedTools)
                    {
                        card.Suit = GameEngine.CardSet[card.Id].Suit;
                    }

                    //reset card type if entering hand or discard
                    if (IsClient && (move.to.DeckType == DeckType.Discard || move.to.DeckType == DeckType.Hand))
                    {
                        card.Log = new ActionLog();
                        if (card.Attributes != null) card.Attributes.Clear();
                    }

                    if (IsClient && (move.to.DeckType == DeckType.Hand && GameClient.SelfId != move.to.Player.Id))
                    {
                        card.Id = -1;
                    }
                    if (move.to.Player != null)
                    {
                        GameEventArgs args = new GameEventArgs();
                        args.Source = move.to.Player;
                        args.Card = card;
                        Emit(GameEvent.EnforcedCardTransform, args);
                    }
                }
                i++;
            }
        }

        public void MoveCards(CardsMovement move, MovementHelper log, bool insertBefore = false)
        {
            List<CardsMovement> moves = new List<CardsMovement>();
            moves.Add(move);
            List<MovementHelper> logs;
            if (log != null)
            {
                logs = new List<MovementHelper>();
                logs.Add(log);
            }
            else
            {
                logs = null;
            }
            MoveCards(moves, logs, new List<bool>() {insertBefore});
        }

        public Card PeekCard(int i)
        {
            var drawDeck = decks[DeckType.Dealing];
            if (i >= drawDeck.Count)
            {
                Emit(GameEvent.Shuffle, new GameEventArgs());
            }
            if (drawDeck.Count == 0)
            {
                throw new GameOverException();
            }
            return drawDeck[i];
        }

        public Card DrawCard()
        {
            var drawDeck = decks[DeckType.Dealing];
            if (drawDeck.Count == 0)
            {
                Emit(GameEvent.Shuffle, new GameEventArgs());
            }
            if (drawDeck.Count == 0)
            {
                throw new GameOverException();
            }
            Card card = drawDeck.First();
            drawDeck.RemoveAt(0);
            return card;
        }

        public void DrawCards(Player player, int num)
        {
            List<Card> cardsDrawn = new List<Card>();
            try
            {
                for (int i = 0; i < num; i++)
                {
                    SyncImmutableCard(player, PeekCard(0));
                    cardsDrawn.Add(DrawCard());
                }
            }
            catch (ArgumentException)
            {
                throw new EndOfDealingDeckException();
            }
            CardsMovement move;
            move.cards = cardsDrawn;
            move.to = new DeckPlace(player, DeckType.Hand);
            MoveCards(move, null);
            PlayerAcquiredCard(player, cardsDrawn);
        }

        Player currentActingPlayer;

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>UI ONLY!</remarks>
        public Player CurrentActingPlayer
        {
            get { return currentActingPlayer; }
            set
            {
                if (currentActingPlayer == value) return;
                currentActingPlayer = value;
                OnPropertyChanged("CurrentActingPlayer");
            }
        }

        Player currentPlayer;

        public Player CurrentPlayer
        {
            get { return currentPlayer; }
            set 
            {
                Trace.Assert(value != null);
                if (currentPlayer != null)
                {
                    var temp = new Dictionary<PlayerAttribute, int>(currentPlayer.Attributes);
                    foreach (var pair in temp)
                    {
                        if (pair.Key.AutoReset)
                        {
                            currentPlayer[pair.Key] = 0;
                        }
                    }
                }
                if (currentPlayer == value) return;
                currentPlayer = value;
                OnPropertyChanged("CurrentPlayer");
            }
        }

        TurnPhase currentPhase;

        public TurnPhase CurrentPhase
        {
            get { return currentPhase; }
            set 
            {
                if (currentPhase == value) return;
                currentPhase = value;
                OnPropertyChanged("CurrentPhase");
            }
        }

        int currentPhaseEventIndex;

        public int CurrentPhaseEventIndex
        {
            get { return currentPhaseEventIndex; }
            set { currentPhaseEventIndex = value; }
        }

        public static Dictionary<TurnPhase, GameEvent>[] PhaseEvents = new Dictionary<TurnPhase, GameEvent>[]
                         { GameEvent.PhaseBeginEvents, GameEvent.PhaseProceedEvents,
                           GameEvent.PhaseEndEvents, GameEvent.PhaseOutEvents };

        /// <summary>
        /// Get player next to the a player in counter-clock seat map. (must be alive)
        /// </summary>
        /// <param name="p">Player</param>
        /// <returns></returns>
        public virtual Player NextAlivePlayer(Player p)
        {
            p = NextPlayer(p);
            while (p.IsDead)
            {
                p = NextPlayer(p);
            }
            return p;
        }

        /// <summary>
        /// Get player next to the a player in counter-clock seat map. (must be alive)
        /// </summary>
        /// <param name="p">Player</param>
        /// <returns></returns>
        public virtual Player NextPlayer(Player p)
        {
            int numPlayers = players.Count;
            int i;
            for (i = 0; i < numPlayers; i++)
            {
                if (players[i] == p)
                {
                    break;
                }
            }

            // The next player to the last player is the first player.
            if (i == numPlayers - 1)
            {
                return players[0];
            }
            else if (i >= numPlayers)
            {
                Trace.Assert(false);
                return null;
            }
            else
            {
                return players[i + 1];
            }
        }

        public virtual int OrderOf(Player withRespectTo, Player target)
        {
            int numPlayers = players.Count;
            int i;
            for (i = 0; i < numPlayers; i++)
            {
                if (players[i] == withRespectTo)
                {
                    break;
                }
            }

            // The next player to the last player is the first player.
            int order = 0;
            while (players[i] != target)
            {
                if (i == numPlayers - 1)
                {
                    i = 0;
                }
                else
                {
                    i = i + 1;
                }
                order++;
            }
            Trace.Assert(order < numPlayers);
            return order;
        }

        public virtual void SortByOrderOfComputation(Player withRespectTo, List<Player> players)
        {
            players.Sort((a, b) =>
                {
                    return OrderOf(withRespectTo, a).CompareTo(OrderOf(withRespectTo, b));
                });
        }

        public virtual bool AllAlive(List<Player> players)
        {
            if (players != null)
            {
                foreach (Player p in players)
                {
                    if (p.IsDead)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Get player previous to the a player in counter-clock seat map. (must be alive)
        /// </summary>
        /// <param name="p">Player</param>
        /// <returns></returns>
        public virtual Player PreviousAlivePlayer(Player p)
        {
            p = PreviousPlayer(p);
            while (p.IsDead)
            {
                p = PreviousPlayer(p);
            }
            return p;
        }

        /// <summary>
        /// Get player previous to a player in counter-clock seat map
        /// </summary>
        /// <param name="p">Player</param>
        /// <returns></returns>
        public virtual Player PreviousPlayer(Player p)
        {
            int numPlayers = players.Count;
            int i;
            for (i = 0; i < numPlayers; i++)
            {
                if (players[i] == p)
                {
                    break;
                }
            }

            // The previous player to the first player is the last player
            if (i == 0)
            {
                return players[numPlayers - 1];
            }
            else if (i >= numPlayers)
            {
                return null;
            }
            else
            {
                return players[i - 1];
            }
        }

        public virtual int DistanceTo(Player from, Player to)
        {
            int distRight = from[Player.RangeMinus], distLeft = from[Player.RangeMinus];
            Player p = from;
            while (p != to)
            {
                p = NextAlivePlayer(p);
                distRight++;
            }
            distRight += to[Player.RangePlus];
            p = from;
            while (p != to)
            {
                p = PreviousAlivePlayer(p);
                distLeft++;
            }
            distLeft += to[Player.RangePlus];
            return distRight > distLeft ? distLeft : distRight;
        }

        /// <summary>
        /// 造成伤害
        /// </summary>
        /// <param name="source">伤害来源</param>
        /// <param name="dest">伤害目标</param>
        /// <param name="magnitude">伤害点数</param>
        /// <param name="elemental">伤害属性</param>
        /// <param name="cards">造成伤害的牌</param>
        public void DoDamage(Player source, Player dest, int magnitude, DamageElement elemental, ICard card, ReadOnlyCard readonlyCard)
        {
            var damageArgs = new DamageEventArgs() { Source = source, Targets = new List<Player>(), Magnitude = magnitude, Element = elemental };
            HealthChangedEventArgs healthChangedArgs;
            int ironShackledDamage = 0;
            DamageElement ironShackledDamageElement = DamageElement.None;
            damageArgs.ReadonlyCard = readonlyCard;
            if (card is CompositeCard)
            {
                if ((card as CompositeCard).Subcards != null)
                {
                    damageArgs.Cards = new List<Card>((card as CompositeCard).Subcards);
                }
            }
            else if (card is Card)
            {
                damageArgs.Cards = new List<Card>() { card as Card };
            }
            else
            {
                damageArgs.Cards = new List<Card>();
            }
            damageArgs.Targets.Add(dest);
            damageArgs.Card = card;

            try
            {
                Game.CurrentGame.Emit(GameEvent.DamageSourceConfirmed, damageArgs);
                Game.CurrentGame.Emit(GameEvent.DamageElementConfirmed, damageArgs);
                Game.CurrentGame.Emit(GameEvent.BeforeDamageComputing, damageArgs);
                Game.CurrentGame.Emit(GameEvent.DamageComputingStarted, damageArgs);
                Game.CurrentGame.Emit(GameEvent.DamageCaused, damageArgs);
                Game.CurrentGame.Emit(GameEvent.DamageInflicted, damageArgs);
                if (damageArgs.Magnitude == 0)
                {
                    Trace.TraceInformation("Damage is 0, aborting");
                    return;
                }
                if (damageArgs.Targets[0].IsIronShackled && damageArgs.Element != DamageElement.None)
                {
                    ironShackledDamage = damageArgs.Magnitude;
                    Trace.TraceInformation("IronShackled damage {0}", ironShackledDamage);
                    ironShackledDamageElement = damageArgs.Element;
                    damageArgs.Targets[0].IsIronShackled = false;
                }  
                healthChangedArgs = new HealthChangedEventArgs(damageArgs);
                Game.CurrentGame.Emit(GameEvent.BeforeHealthChanged, healthChangedArgs);
                damageArgs.Magnitude = -healthChangedArgs.Delta;
            }
            catch (TriggerResultException e)
            {
                if (e.Status == TriggerResult.End)
                {
                    Trace.TraceInformation("Damage Aborted");
                    return;
                }
                Trace.Assert(false);
                return;
            }

            Trace.Assert(damageArgs.Targets.Count == 1);
            damageArgs.Targets[0].Health -= damageArgs.Magnitude;
            Trace.TraceInformation("Player {0} Lose {1} hp, @ {2} hp", damageArgs.Targets[0].Id, damageArgs.Magnitude, damageArgs.Targets[0].Health);
            NotificationProxy.NotifyDamage(source, damageArgs.Targets[0], damageArgs.Magnitude, damageArgs.Element);

            try
            {
                Game.CurrentGame.Emit(GameEvent.AfterHealthChanged, healthChangedArgs);
            }
            catch (TriggerResultException)
            {
            }
            Game.CurrentGame.Emit(GameEvent.AfterDamageCaused, damageArgs);
            Game.CurrentGame.Emit(GameEvent.AfterDamageInflicted, damageArgs);
            Game.CurrentGame.Emit(GameEvent.DamageComputingFinished, damageArgs);
            if (ironShackledDamage != 0)
            {
                List<Player> toProcess = new List<Player>(Game.CurrentGame.AlivePlayers);
                Game.CurrentGame.SortByOrderOfComputation(damageArgs.Targets[0], toProcess);
                foreach (Player p in toProcess)
                {
                    if (p.IsIronShackled)
                    {
                        DoDamage(damageArgs.Source, p, ironShackledDamage, ironShackledDamageElement, card, readonlyCard);
                    }
                }
            }
        }

        public ReadOnlyCard Judge(Player player, ISkill skill = null, ICard handler = null)
        {
            CardsMovement move = new CardsMovement();
            Card c;
            if (decks[player, DeckType.JudgeResult].Count != 0)
            {
                c = decks[player, DeckType.JudgeResult][0];
                move = new CardsMovement();
                move.cards = new List<Card>();
                List<Card> backup = new List<Card>(move.cards);
                move.cards.Add(c);
                move.to = new DeckPlace(null, DeckType.Discard);
                PlayerAboutToDiscardCard(player, move.cards, DiscardReason.Judge);
                MoveCards(move, null);
                PlayerDiscardedCard(player, backup, DiscardReason.Judge);
            }
            SyncImmutableCardAll(PeekCard(0));
            c = Game.CurrentGame.DrawCard();
            move = new CardsMovement();
            move.cards = new List<Card>();
            move.cards.Add(c);
            move.to = new DeckPlace(player, DeckType.JudgeResult);
            MoveCards(move, null);
            GameEventArgs args = new GameEventArgs();
            args.Source = player;
            Game.CurrentGame.Emit(GameEvent.PlayerJudgeBegin, args);
            c = Game.CurrentGame.Decks[player, DeckType.JudgeResult][0];
            args.Card = new ReadOnlyCard(c);
            args.Cards = new List<Card>() { c };
            Game.CurrentGame.Emit(GameEvent.PlayerJudgeDone, args);
            Trace.Assert(args.Source == player);
            Trace.Assert(args.Card is ReadOnlyCard);

            ActionLog log = new ActionLog();
            log.SkillAction = skill;
            log.CardAction = handler;
            log.Source = player;
            Game.CurrentGame.NotificationProxy.NotifyJudge(player, args.Cards[0], log);

            if (decks[player, DeckType.JudgeResult].Count != 0)
            {
                c = decks[player, DeckType.JudgeResult][0];
                move = new CardsMovement();
                move.cards = new List<Card>();
                List<Card> backup = new List<Card>(move.cards);
                move.cards.Add(c);
                move.to = new DeckPlace(null, DeckType.Discard);
                PlayerAboutToDiscardCard(player, move.cards, DiscardReason.Judge);
                MoveCards(move, null);
                PlayerDiscardedCard(player, backup, DiscardReason.Judge);
            }
            return args.Card as ReadOnlyCard;
        }

        public void RecoverHealth(Player source, Player target, int magnitude)
        {
            if (target.Health >= target.MaxHealth)
            {
                return;
            }
            var args = new HealthChangedEventArgs() { Source = source, Delta = magnitude};
            args.Targets.Add(target);

            Game.CurrentGame.Emit(GameEvent.BeforeHealthChanged, args);

            Trace.Assert(args.Targets.Count == 1);
            if (args.Targets[0].Health + args.Delta > args.Targets[0].MaxHealth)
            {
                args.Targets[0].Health = args.Targets[0].MaxHealth;
            }
            else
            {
                args.Targets[0].Health += args.Delta;
            }
            
            Trace.TraceInformation("Player {0} gain {1} hp, @ {2} hp", args.Targets[0].Id, args.Delta, args.Targets[0].Health);

            try
            {
                Game.CurrentGame.Emit(GameEvent.AfterHealthChanged, args);
            }
            catch (TriggerResultException)
            {
            }
        }

        public void LoseHealth(Player source, int magnitude)
        {
            var args = new HealthChangedEventArgs() { Source = source, Delta = -magnitude};
            args.Targets.Add(source);

            Game.CurrentGame.Emit(GameEvent.BeforeHealthChanged, args);

            Trace.Assert(args.Targets.Count == 1);
            args.Targets[0].Health += args.Delta;
            Trace.TraceInformation("Player {0} lose {1} hp, @ {2} hp", args.Targets[0].Id, -args.Delta, args.Targets[0].Health);
            Game.CurrentGame.NotificationProxy.NotifyLoseHealth(args.Targets[0], -args.Delta);

            try
            {
                Game.CurrentGame.Emit(GameEvent.AfterHealthChanged, args);
            }
            catch (TriggerResultException)
            {
            }

        }

        public void LoseMaxHealth(Player source, int magnitude)
        {
            int result = source.MaxHealth - magnitude;
            if (source.Health > result) source.Health = result;
            source.MaxHealth = result;
            if (source.MaxHealth <= 0) Game.CurrentGame.Emit(GameEvent.PlayerIsDead, new GameEventArgs() { Source = null, Targets = new List<Player>() { source } });
        }

        /// <summary>
        /// 处理玩家打出卡牌事件。
        /// </summary>
        /// <param name="source"></param>
        /// <param name="c"></param>
        public void PlayerPlayedCard(Player source, ICard c)
        {
            try
            {
                GameEventArgs arg = new GameEventArgs();
                arg.Source = source;
                arg.Targets = null;
                arg.Card = c;

                Emit(GameEvent.PlayerPlayedCard, arg);
            }
            catch (TriggerResultException)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 处理玩家打出卡牌
        /// </summary>
        /// <param name="p"></param>
        /// <param name="skill"></param>
        /// <param name="cards"></param>
        /// <param name="targets"></param>
        /// <returns></returns>
        public bool HandleCardPlay(Player p, ISkill skill, List<Card> cards, List<Player> targets)
        {
            Trace.Assert(cards != null);
            CardsMovement m;
            ICard result;
            bool status = CommitCardTransform(p, skill, cards, out result, targets);
            if (!status)
            {
                return false;
            }
            if (skill != null)
            {
                var r = result as CompositeCard;
                Trace.Assert(r != null);
                cards.Clear();
                cards.AddRange(r.Subcards);
            }
            m.cards = new List<Card>(cards);
            m.to = new DeckPlace(null, DeckType.Discard);
            Player isDoingAFavor = p;
            foreach (var checkFavor in m.cards)
            {
                if (checkFavor.Owner != p)
                {
                    Trace.TraceInformation("Acting on behalf of others");
                    isDoingAFavor = checkFavor.Owner;
                    break;
                }
            }
            result.Type.TagAndNotify(p, targets, result, GameAction.Play);
            List<Card> backup = new List<Card>(m.cards);
            if (isDoingAFavor != p)
            {
                PlayerAboutToDiscardCard(isDoingAFavor, m.cards, DiscardReason.Play);
                MoveCards(m, null);
                PlayerPlayedCard(isDoingAFavor, result);
                PlayerPlayedCard(p, result);
                PlayerDiscardedCard(isDoingAFavor, backup, DiscardReason.Play);
            }
            else
            {
                PlayerAboutToDiscardCard(p, m.cards, DiscardReason.Play);
                MoveCards(m, null);
                PlayerPlayedCard(p, result);
                PlayerDiscardedCard(p, backup, DiscardReason.Play);
            }
            return true;
        }

        public void PlayerDiscardedCard(Player p, List<Card> cards, DiscardReason reason)
        {
            try
            {
                var arg = new DiscardCardEventArgs();
                arg.Source = p;
                arg.Targets = null;
                arg.Cards = cards;
                arg.Reason = reason;
                Emit(GameEvent.CardsEnteredDiscardDeck, arg);
            }
            catch (TriggerResultException)
            {
                throw new NotImplementedException();
            }
        }

        public void PlayerAboutToDiscardCard(Player p, List<Card> cards, DiscardReason reason)
        {
            SyncCardsAll(cards);
            try
            {
                var arg = new DiscardCardEventArgs();
                arg.Source = p;
                arg.Targets = null;
                arg.Cards = cards;
                arg.Reason = reason;
                Emit(GameEvent.CardsEnteringDiscardDeck, arg, true);
            }
            catch (TriggerResultException)
            {
                throw new NotImplementedException();
            }
        }

        public void PlayerLostCard(Player p, List<Card> cards)
        {
            try
            {
                GameEventArgs arg = new GameEventArgs();
                arg.Source = p;
                arg.Targets = null;
                arg.Cards = cards;
                Emit(GameEvent.CardsLost, arg);
            }
            catch (TriggerResultException)
            {
                throw new NotImplementedException();
            }
        }

        public void PlayerAcquiredCard(Player p, List<Card> cards)
        {
            try
            {
                GameEventArgs arg = new GameEventArgs();
                arg.Source = p;
                arg.Targets = null;
                arg.Cards = cards;
                Emit(GameEvent.CardsAcquired, arg);
            }
            catch (TriggerResultException)
            {
                throw new NotImplementedException();
            }
        }

        public void HandleCardDiscard(Player p, List<Card> cards, DiscardReason reason = DiscardReason.Discard)
        {
            CardsMovement move;
            move.cards = new List<Card>(cards);
            foreach (Card c in cards)
            {
                c.Log.Source = p;
                if (reason == DiscardReason.Discard)
                    c.Log.GameAction = GameAction.Discard;
                else if (reason == DiscardReason.Play)
                    c.Log.GameAction = GameAction.Play;
                else if (reason == DiscardReason.Use)
                    c.Log.GameAction = GameAction.Use;
            }
            List<Card> backup = new List<Card>(move.cards);
            move.to = new DeckPlace(null, DeckType.Discard);
            PlayerAboutToDiscardCard(p, move.cards, reason);
            MoveCards(move, null);
            PlayerLostCard(p, backup);
            PlayerDiscardedCard(p, backup, reason);
        }

        public void HandleCardTransferToHand(Player from, Player to, List<Card> cards, MovementHelper helper = null)
        {
            CardsMovement move;
            move.cards = new List<Card>(cards);
            move.to = new DeckPlace(to, DeckType.Hand);
            MoveCards(move, helper);
            PlayerLostCard(from, cards);
            PlayerAcquiredCard(to, cards);
        }

        public bool CommitCardTransform(Player p, ISkill skill, List<Card> cards, out ICard result, List<Player> targets)
        {
            if (skill != null)
            {
                CompositeCard card;
                CardTransformSkill s = (CardTransformSkill)skill;                
                if (!s.Transform(cards, null, out card, targets))
                {
                    result = null;
                    return false;
                }
                result = card;
            }
            else
            {
                result = cards[0];
            }
            return true;
        }

        public bool PlayerCanDiscardCard(Player p, Card c)
        {
            GameEventArgs arg = new GameEventArgs();
            arg.Source = p;
            arg.Card = c;
            try
            {
                Game.CurrentGame.Emit(GameEvent.PlayerCanDiscardCard, arg);
            }
            catch (TriggerResultException e)
            {
                if (e.Status == TriggerResult.Fail)
                {
                    Trace.TraceInformation("Player {0} cannot discard {1}", p.Id, c.Type.CardType);
                    return false;
                }
                else
                {
                    Trace.Assert(false);
                }
            }
            return true;
        }

        public bool PlayerCanUseCard(Player p, ICard c)
        {
            GameEventArgs arg = new GameEventArgs();
            arg.Source = p;
            arg.Card = c;
            try
            {
                Game.CurrentGame.Emit(GameEvent.PlayerCanUseCard, arg);
            }
            catch (TriggerResultException e)
            {
                if (e.Status == TriggerResult.Fail)
                {
                    Trace.TraceInformation("Player {0} cannot use {1}", p.Id, c.Type.CardType);
                    return false;
                }
                else
                {
                    Trace.Assert(false);
                }
            }
            return true;
        }

        public bool PlayerCanPlayCard(Player p, ICard c)
        {
            GameEventArgs arg = new GameEventArgs();
            arg.Source = p;
            arg.Card = c;
            try
            {
                Game.CurrentGame.Emit(GameEvent.PlayerCanPlayCard, arg);
            }
            catch (TriggerResultException e)
            {
                if (e.Status == TriggerResult.Fail)
                {
                    Trace.TraceInformation("Player {0} cannot play {1}", p.Id, c.Type.CardType);
                    return false;
                }
                else
                {
                    Trace.Assert(false);
                }
            }
            return true;
        }

        public bool PlayerCanDiscardCards(Player p, List<Card> cards)
        {
            foreach (Card c in cards)
            {
                if (!PlayerCanDiscardCard(p, c))
                {
                    return false;
                }
            }
            return true;
        }

        public bool PlayerCanBeTargeted(Player source, List<Player> targets, ICard card)
        {
            GameEventArgs arg = new GameEventArgs();
            arg.Source = source;
            arg.Targets = targets;
            arg.Card = card;
            try
            {
                Game.CurrentGame.Emit(GameEvent.PlayerCanBeTargeted, arg);
                return true;
            }
            catch (TriggerResultException e)
            {
                if (e.Status == TriggerResult.Fail)
                {
                    Trace.TraceInformation("Players cannot be targeted by {0}", card.Type.CardType);
                    return false;
                }
                else
                {
                    Trace.Assert(false);
                }
            }
            return true;
        }

        Stack<Player> isDying;

        public Stack<Player> IsDying
        {
            get { return isDying; }
            set { isDying = value; }
        }

        public class PlayerHpChanged : Trigger
        {
            public override void Run(GameEvent gameEvent, GameEventArgs eventArgs)
            {
                Trace.Assert(eventArgs.Targets.Count == 1);
                Player target = eventArgs.Targets[0];
                if (target.Health <= 0)
                {
                    Trace.TraceInformation("Player {0} dying", target.Id);
                    GameEventArgs args = new GameEventArgs();
                    args.Source = eventArgs.Source;
                    args.Targets = new List<Player>() {target};
                    try
                    {
                        Game.CurrentGame.Emit(GameEvent.PlayerIsAboutToDie, args);
                    }
                    catch (TriggerResultException)
                    {
                    }
                    if (target.Health <= 0)
                    {
                        try
                        {
                            Game.CurrentGame.Emit(GameEvent.PlayerDying, args);
                        }
                        catch (TriggerResultException)
                        {
                        }
                    }
                }
            }
        }

        public delegate int NumberOfCardsToForcePlayerDiscard(Player p, int discarded);

        private class PlayerForceDiscardVerifier : ICardUsageVerifier
        {
            public UiHelper Helper { get { return new UiHelper(); } }

            public VerifierResult FastVerify(Player source, ISkill skill, List<Card> cards, List<Player> players)
            {
                if (skill != null)
                {
                    return VerifierResult.Fail;
                }
                if (players != null && players.Count > 0)
                {
                    return VerifierResult.Fail;
                }
                if (cards == null || cards.Count == 0)
                {
                    return VerifierResult.Partial;
                }
                foreach (Card c in cards)
                {
                    if (!Game.CurrentGame.PlayerCanDiscardCard(source, c))
                    {
                        return VerifierResult.Fail;
                    }
                    if (!canDiscardEquip && c.Place.DeckType != DeckType.Hand)
                    {
                        return VerifierResult.Fail;
                    }
                }
                if (cards.Count > toDiscard)
                {
                    return VerifierResult.Fail;
                }
                return VerifierResult.Success;
            }

            public IList<CardHandler> AcceptableCardTypes
            {
                get { throw new NotImplementedException(); }
            }

            public VerifierResult Verify(Player source, ISkill skill, List<Card> cards, List<Player> players)
            {
                return FastVerify(source, skill, cards, players);
            }

            int toDiscard;
            bool canDiscardEquip;
            public PlayerForceDiscardVerifier(int n, bool equip)
            {
                toDiscard = n;
                canDiscardEquip = equip;
            }
        }

        public void ForcePlayerDiscard(Player player, NumberOfCardsToForcePlayerDiscard numberOfCards, bool canDiscardEquipment)
        {
            Trace.TraceInformation("Player {0} discard.", player);
            int cannotBeDiscarded = 0;
            int numberOfCardsDiscarded = 0;
            while (true)
            {
                int handCardCount = Game.CurrentGame.Decks[player, DeckType.Hand].Count; // 玩家手牌数
                int equipCardCount = Game.CurrentGame.Decks[player, DeckType.Equipment].Count; // 玩家装备牌数
                int toDiscard = numberOfCards(player, numberOfCardsDiscarded);
                // Have we finished discarding everything?
                // We finish if 
                //      玩家手牌数 小于等于 我们要强制弃掉的数目
                //  或者玩家手牌数 (小于)等于 不可弃的牌的数目（此时装备若可弃，必须弃光）
                if (toDiscard == 0 || (handCardCount <= cannotBeDiscarded && (!canDiscardEquipment || equipCardCount == 0)))
                {
                    break;
                }
                Trace.Assert(Game.CurrentGame.UiProxies.ContainsKey(player));
                IUiProxy proxy = Game.CurrentGame.UiProxies[player];
                ISkill skill;
                List<Card> cards;
                List<Player> players;
                PlayerForceDiscardVerifier v = new PlayerForceDiscardVerifier(toDiscard, canDiscardEquipment);
                cannotBeDiscarded = 0;
                foreach (Card c in Game.CurrentGame.Decks[player, DeckType.Hand])
                {
                    if (!Game.CurrentGame.PlayerCanDiscardCard(player, c))
                    {
                        cannotBeDiscarded++;
                    }
                }
                //如果玩家无法达到弃牌要求 则 摊牌
                bool status = (canDiscardEquipment ? equipCardCount : 0) + handCardCount - toDiscard >= cannotBeDiscarded;
                Game.CurrentGame.SyncConfirmationStatus(ref status);
                if (!status)
                {
                    Game.CurrentGame.SyncImmutableCardsAll(Game.CurrentGame.Decks[player, DeckType.Hand]);
                }

                if (!proxy.AskForCardUsage(new Prompt(Prompt.DiscardPhasePrompt, toDiscard),
                                           v, out skill, out cards, out players))
                {
                    //玩家没有回应(default)
                    //如果玩家有不可弃掉的牌(这个只有服务器知道） 则通知所有客户端该玩家手牌
                    status = (cannotBeDiscarded == 0);
                    Game.CurrentGame.SyncConfirmationStatus(ref status);
                    if (!status)
                    {
                        Game.CurrentGame.SyncImmutableCardsAll(Game.CurrentGame.Decks[player, DeckType.Hand]);
                    }
                    cannotBeDiscarded = 0;
                    foreach (Card c in Game.CurrentGame.Decks[player, DeckType.Hand])
                    {
                        if (!Game.CurrentGame.PlayerCanDiscardCard(player, c))
                        {
                            cannotBeDiscarded++;
                        }
                    }

                    Trace.TraceInformation("Invalid answer, choosing for you");
                    cards = new List<Card>();
                    int cardsDiscarded = 0;
                    var chooseFrom = new List<Card>(Game.CurrentGame.Decks[player, DeckType.Hand]);
                    if (canDiscardEquipment)
                    {
                        chooseFrom.AddRange(Game.CurrentGame.Decks[player, DeckType.Equipment]);
                    }
                    foreach (Card c in chooseFrom)
                    {
                        if (Game.CurrentGame.PlayerCanDiscardCard(player, c))
                        {
                            cards.Add(c);
                            cardsDiscarded++;
                        }
                        if (cardsDiscarded == toDiscard)
                        {
                            SyncCardsAll(cards);
                            break;
                        }
                    }
                }
                numberOfCardsDiscarded += cards.Count;
                Game.CurrentGame.HandleCardDiscard(player, cards);
            }
        }


        public void InsertBeforeDeal(Player target, List<Card> list, MovementHelper helper = null)
        {
            CardsMovement move = new CardsMovement();
            move.cards = new List<Card>(list);
            move.to = new DeckPlace(null, DeckType.Dealing);
            MoveCards(move, helper, true);
            if (target != null)
            {
                PlayerLostCard(target, list);
            }
        }

        public void PlaceIntoDiscard(Player target, List<Card> list)
        {
            CardsMovement move = new CardsMovement();
            move.cards = new List<Card>(list);
            move.to = new DeckPlace(null, DeckType.Discard);
            MoveCards(move, null);
            if (target != null)
            {
                PlayerLostCard(target, list);
            }
        }

        public bool PinDian(Player from, Player to)
        {
            Dictionary<Player, ISkill> aSkill;
            Dictionary<Player, List<Card>> aCards;
            Dictionary<Player, List<Player>> aPlayers;

            GlobalProxy.AskForMultipleCardUsage(new CardUsagePrompt("PinDian"), new PinDianVerifier(), new List<Player>() { from, to }, out aSkill, out aCards, out aPlayers);
            Card card1, card2;
            if (!aCards.ContainsKey(from) || aCards[from].Count == 0)
            {
                card1 = Game.CurrentGame.Decks[from, DeckType.Hand][0];
                SyncImmutableCardAll(card1);
            }
            else
            {
                card1 = aCards[from][0];
            }
            if (!aCards.ContainsKey(to) || aCards[to].Count == 0)
            {
                card2 = Game.CurrentGame.Decks[to, DeckType.Hand][0];
                SyncImmutableCardAll(card2);
            }
            else
            {
                card2 = aCards[to][0];
            }
            EnterAtomicContext();
            PlaceIntoDiscard(from, new List<Card>() { card1 });
            PlaceIntoDiscard(from, new List<Card>() { card2 });
            PlayerLostCard(from, new List<Card>() { card1 });
            PlayerLostCard(from, new List<Card>() { card2 });
            ExitAtomicContext();
            return card1.Rank > card2.Rank;
        }

        public class PinDianVerifier : ICardUsageVerifier
        {
            public VerifierResult FastVerify(Player source, ISkill skill, List<Card> cards, List<Player> players)
            {
                if (skill != null || (players != null && players.Count > 0))
                {
                    return VerifierResult.Fail;
                }
                if (cards == null || cards.Count == 0)
                {
                    return VerifierResult.Partial;
                }
                if (cards.Count > 1)
                {
                    return VerifierResult.Fail;
                }
                if (cards[0].Place.DeckType != DeckType.Hand)
                {
                    return VerifierResult.Fail;
                }
                return VerifierResult.Success;
            }

            public IList<CardHandler> AcceptableCardTypes
            {
                get { return null; }
            }

            public VerifierResult Verify(Player source, ISkill skill, List<Card> cards, List<Player> players)
            {
                return FastVerify(source, skill, cards, players);
            }

            public UiHelper Helper
            {
                get { return new UiHelper(); }
            }
        }

        public void MoveHandCard(Player player, int from, int to)
        {
            if (IsClient && player.Id == GameClient.SelfId)
            {
                GameClient.MoveHandCard(from, to);
            }
        }

        public void DoPlayer(Player p)
        {
            var phase = CurrentPhase;
            var index = CurrentPhaseEventIndex;
            var player = CurrentPlayer;
            CurrentPhaseEventIndex = 0;
            CurrentPhase = TurnPhase.Start;
            Emit(GameEvent.DoPlayer, new GameEventArgs() { Source = p });
            CurrentPhase = phase;
            CurrentPhaseEventIndex = index;
            CurrentPlayer = player;
        }
    }
}

