﻿#define NETWORKING
#define CLIENTLOG
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Sanguosha.UI.Controls;
using Sanguosha.Core.Heroes;
using Sanguosha.Core.Players;
using Sanguosha.Core.Games;
using Sanguosha.Core.UI;
using System.Threading;
using Sanguosha.Core.Network;
using System.Diagnostics;
using System.IO;
using System.Windows.Navigation;
using System.Collections.ObjectModel;
using Sanguosha.Core.Cards;

namespace Sanguosha.UI.Main
{
    /// <summary>
    /// Interaction logic for MainGame.xaml
    /// </summary>
    public partial class MainGame : Page
    {
        public MainGame()
        {
            this.InitializeComponent();
            ctrlGetCard.OnCardSelected += new CardSelectedHandler(ctrlGetCard_OnCardSelected);
            ctrlGetSkill.OnSkillNameSelected += new SkillNameSelectedHandler(ctrlGetSkill_OnSkillNameSelected);
            // Insert code required on object creation below this point.
        }

        void ctrlGetSkill_OnSkillNameSelected(string skillName)
        {
            var gameModel = (gameView.DataContext as GameViewModel);
            gameModel.MainPlayerModel.CheatGetSkill(skillName);
        }

        void ctrlGetCard_OnCardSelected(Card card)
        {
            var gameModel = (gameView.DataContext as GameViewModel);
            gameModel.MainPlayerModel.CheatGetCard(card);
        }

        private Client _networkClient;

        public Client NetworkClient
        {
            get { return _networkClient; }
            set { _networkClient = value; }
        }

        int _mainSeat;

        public int MainSeat
        {
            get { return _mainSeat; }
            set { _mainSeat = value; }
        }

        const int numberOfHeros = 3;

        private void InitGame()
        {
#if CLIENTLOG
            TextWriterTraceListener twtl = new TextWriterTraceListener(System.IO.Path.Combine(Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.FriendlyName + DateTime.Now.Hour.ToString() + DateTime.Now.Minute.ToString() + DateTime.Now.Second.ToString() + ".txt"));
            twtl.Name = "TextLogger";
            twtl.TraceOutputOptions = TraceOptions.ThreadId | TraceOptions.DateTime;

            ConsoleTraceListener ctl = new ConsoleTraceListener(false);
            ctl.TraceOutputOptions = TraceOptions.DateTime;

            Trace.Listeners.Add(twtl);
            Trace.Listeners.Add(ctl);
            Trace.AutoFlush = true;

            Trace.WriteLine("Log starting");
#endif

            _game = new RoleGame(1);
            foreach (var g in GameEngine.Expansions.Values)
            {
                _game.LoadExpansion(g);
            }
#if NETWORKING
            ClientNetworkUiProxy activeClientProxy = null;
            for (int i = 0; i < numberOfHeros; i++)
#else
            for (int i = 0; i < 8; i++)
#endif
            {
                Player player = new Player();
                player.Id = i;
                _game.Players.Add(player);
                if (i == 1)
                {
                    player.IsFemale = true;
                }
                else
                {
                    player.IsMale = true;
                }
            }
#if NETWORKING
            _game.GameClient = NetworkClient;
#else
            _game.GlobalProxy = new GlobalDummyProxy();
#endif
            GameViewModel gameModel = new GameViewModel();
            gameModel.Game = _game;
            gameModel.MainPlayerSeatNumber = MainSeat;
            gameView.DataContext = gameModel;
            _game.NotificationProxy = gameView;
            List<ClientNetworkUiProxy> inactive = new List<ClientNetworkUiProxy>();
            for (int i = 0; i < _game.Players.Count; i++)
            {
                var player = gameModel.PlayerModels[i].Player;                
#if NETWORKING
                var proxy = new ClientNetworkUiProxy(
                            new AsyncUiAdapter(gameModel.PlayerModels[i]), NetworkClient, i == 0);
                proxy.HostPlayer = player;
                proxy.TimeOutSeconds = 15;
                if (i == 0)
                {
                    activeClientProxy = proxy;
                }
                else
                {
                    inactive.Add(proxy);
                }
#else
                var proxy = new AsyncUiAdapter(gameModel.PlayerModels[i]);
#endif
                _game.UiProxies.Add(player, proxy);
            }
#if NETWORKING
            _game.GlobalProxy = new GlobalClientUiProxy(_game, activeClientProxy, inactive);
#endif
        }

        private Game _game;
        
        Thread gameThread;

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            InitGame();
            gameThread = new Thread(_game.Run) { IsBackground = true };
            gameThread.Start();
        }

        private void btnGetCard_Click(object sender, RoutedEventArgs e)
        {
            windowGetCard.Show();
            ObservableCollection<CardViewModel> model = new ObservableCollection<CardViewModel>();

            foreach (var card in _game.OriginalCardSet)
            {
                if (card.Id > 0 && !(card.Type is HeroCardHandler) && !(card.Type is RoleCardHandler))
                {
                    model.Add(new CardViewModel() { Card = card });
                }
            }
            ctrlGetCard.DataContext = model;
            
        }

        private void btnGetSkill_Click(object sender, RoutedEventArgs e)
        {
            windowGetSkill.Show();
            ObservableCollection<HeroViewModel> model = new ObservableCollection<HeroViewModel>();

            foreach (var card in _game.OriginalCardSet)
            {
                if (card.Id > 0 && (card.Type is HeroCardHandler))
                {
                    string exp = string.Empty;
                    var exps = from expansion in GameEngine.Expansions.Keys
                               where GameEngine.Expansions[expansion].CardSet.Contains(card)
                               select expansion;                    
                    if (exps.Count() > 0)
                    {
                        exp = exps.First();
                    }
                    model.Add(new HeroViewModel() 
                    {
                        Id = card.Id,
                        Hero = (card.Type as HeroCardHandler).Hero,                        
                        ExpansionName = exp
                    });
                }
            }
            ctrlGetSkill.DataContext = model;
        }

    }
}
