﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows;
using System.Diagnostics;
using System.Windows.Input;


namespace Sanguosha.UI.Controls
{

    public delegate void HandCardMovedHandler(int from, int to);
    public class CardStack : Canvas
    {
        #region Constructors
        public CardStack()
        {            
            CardAlignment = HorizontalAlignment.Center;
            IsCardConsumer = false;
            CardCapacity = int.MaxValue;
            _cards = new List<CardView>();
            this.SizeChanged += new SizeChangedEventHandler(CardStack_SizeChanged);
            _rearrangeLock = new object();

            cardBeginDragHandler = new EventHandler(card_OnDragBegin);
            cardDraggingHandler = new EventHandler(card_OnDragging);
            cardEndDragHandler = new EventHandler(card_OnDragEnd);
            cardMouseEnterHandler = new MouseEventHandler(card_MouseEnter);
            cardMouseLeaveHandler = new MouseEventHandler(card_MouseLeave);
        }

        void CardStack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RearrangeCards(0d);
        }

        #endregion

        #region Card Rearrangement

        public void RearrangeCards(double transitionInSeconds)
        {
            var tmpCards = new List<CardView>(_cards);
            if (_interactingCard != null && _cardInteraction == CardInteraction.Drag)
            {
                int gapPos = _ComputeDragCardNewIndex();
                Trace.Assert(gapPos >= 0 && gapPos < _cards.Count);
                tmpCards.Remove(_interactingCard);
                tmpCards.Insert(gapPos, _interactingCard);
            }
            RearrangeCards(tmpCards, transitionInSeconds);
        }

        private static double _extraSpaceForHighlightedCard = 30d;

        static object _rearrangeLock;
        /// <summary>
        /// Arrange children cards in this stack.
        /// </summary>
        /// <remarks>
        /// Assumes that all cards have the same width.
        /// </remarks>
        protected void RearrangeCards(IList<CardView> cards, double transitionInSeconds)
        {
            lock (_rearrangeLock)
            {                
                int numCards = cards.Count();
                if (_cardInteraction == CardInteraction.Drag && cards.Contains(_interactingCard))
                {
                    numCards--;
                }
                if (numCards == 0) return;
                Trace.Assert(ParentGameView != null && ParentGameView.GlobalCanvas != null);
                double cardHeight = (from c in cards select c.Height).Max();
                double cardWidth = (from c in cards select c.Width).Max();
                if (KeepHorizontalOrder)
                {
                    cards = new List<CardView>(cards.OrderBy(c => c.Position.X));
                }

                int zindex = 0;
                double totalWidth = this.ActualWidth;
                
                double extraSpace = Math.Min(MaxCardSpacing - (totalWidth - cardWidth) / (numCards - 1), _extraSpaceForHighlightedCard);
                int highlightIndex = (_interactingCard == null) ? -1 : cards.IndexOf(_interactingCard);
                bool doHighlight = (_cardInteraction != CardInteraction.None && highlightIndex >= 0 && highlightIndex != _cards.Count - 1);

                double maxWidth = doHighlight ? Math.Max(0, totalWidth - extraSpace) : totalWidth;
                double step = Math.Min(MaxCardSpacing, (maxWidth - cardWidth) / (numCards - 1));                

                if (step == MaxCardSpacing) doHighlight = false;

                Point topLeft = this.TranslatePoint(new Point(0,0), ParentGameView.GlobalCanvas);
                double startX = topLeft.X;
                if (CardAlignment == HorizontalAlignment.Center)
                {
                    startX += totalWidth / 2 - step * ((numCards - 1) / 2.0) - cardWidth / 2;
                    if (doHighlight) startX -= _extraSpaceForHighlightedCard / 2;
                }
                else if (CardAlignment == HorizontalAlignment.Right)
                {
                    startX += maxWidth - step * numCards;
                }

                int i = 0;
                double lastX = startX - step;
                foreach (CardView card in cards)
                {                    
                    if (card == _interactingCard && _cardInteraction == CardInteraction.Drag)
                    {
                        i++;
                        double overlap = lastX + step - card.Position.X;
                        if (overlap < 0)
                        {
                            continue;
                        }
                        else if (overlap < cardWidth / 2)
                        {
                            lastX += overlap;
                        }
                        else
                        {
                            lastX = card.Position.X;
                        }
                        continue;
                    }

                    double newX = lastX + step;
                    if (doHighlight && i == highlightIndex + 1)
                    {
                        newX = Math.Min(newX + _extraSpaceForHighlightedCard, lastX + MaxCardSpacing);
                    }
                    lastX = newX;
                    if (!ParentGameView.GlobalCanvas.Children.Contains(card))
                    {
                        ParentGameView.GlobalCanvas.Children.Add(card);
                    }
                    card.Position = new Point(newX, topLeft.Y + ActualHeight / 2 - cardHeight / 2);
                    card.SetValue(Canvas.ZIndexProperty, zindex++);
                    card.Rebase(transitionInSeconds);
                    i++;
                }
            }
        }        

        #endregion

        #region Public Functions

        public void AddCards(IList<CardView> cards, double transitionInSeconds)
        {
            lock (_cards)
            {
                foreach (var card in cards)
                {
                    card.CardModel.IsSelected = false;                    
                    if (IsCardConsumer)
                    {
                        card.Disappear(transitionInSeconds);
                    }
                    else
                    {
                        card.Appear(transitionInSeconds);
                        _cards.Add(card);
                        card.OnDragBegin += cardBeginDragHandler;
                        card.OnDragging += cardDraggingHandler;
                        card.OnDragEnd += cardEndDragHandler;
                        card.MouseEnter += cardMouseEnterHandler;
                        card.MouseLeave += cardMouseLeaveHandler;
                    }
                }
                if (IsCardConsumer)
                {
                    RearrangeCards(cards, transitionInSeconds);
                }
                else
                {
                    RearrangeCards(_cards, transitionInSeconds);
                }
            }
        }

        public void RemoveCards(IList<CardView> cards)
        {
            lock (_cards)
            {
                foreach (var card in cards)
                {
                    if (card == _interactingCard)
                    {
                        if (_cardInteraction == CardInteraction.Drag)
                        {
                            card_OnDragEndUnlock();
                        }
                        if (_cardInteraction == CardInteraction.MouseMove)
                        {
                            card_MouseLeaveUnlock();
                        }
                    }
                    card.OnDragBegin -= cardBeginDragHandler;
                    card.OnDragging -= cardDraggingHandler;
                    card.OnDragEnd -= cardEndDragHandler;
                    card.MouseEnter -= cardMouseEnterHandler;
                    card.MouseLeave -= cardMouseLeaveHandler;
                }
                var nonexisted = from c in cards
                                 where !_cards.Contains(c)
                                 select c;
                RearrangeCards(new List<CardView>(nonexisted), 0d);
                _cards = new List<CardView>(_cards.Except(cards));
                RearrangeCards(_cards, 0.5d);
            }
        }

        #endregion

        #region Drag and Drop, Highlighting
        private CardView _interactingCard;
        private enum CardInteraction
        {
            None,
            MouseMove,
            Drag
        }

        private CardInteraction _cardInteraction;

        private MouseEventHandler cardMouseLeaveHandler;
        private MouseEventHandler cardMouseEnterHandler;
        private EventHandler cardBeginDragHandler;
        private EventHandler cardDraggingHandler;
        private EventHandler cardEndDragHandler;

        void card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_cardInteraction == CardInteraction.MouseMove)
            {
                lock (_cards)
                {
                    Trace.TraceInformation("MouseLeave");
                    card_MouseLeaveUnlock();
                }
            }
        }

        private void card_MouseLeaveUnlock()
        {
            _cardInteraction = CardInteraction.None;
            _interactingCard = null;
            RearrangeCards(0.2d);
        }

        void card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_cardInteraction == CardInteraction.None)
            {
                lock (_cards)
                {
                    Trace.TraceInformation("MouseEnter");
                    _interactingCard = sender as CardView;
                    if (_interactingCard != null)
                    {
                        _cardInteraction = CardInteraction.MouseMove;
                        RearrangeCards(0.2d);
                    }
                }
            }
        }

        int _ComputeDragCardNewIndex()
        {
            Trace.Assert(_interactingCard != null);
            lock (_rearrangeLock)
            {
                double right = _interactingCard.Position.X + _interactingCard.Width;
                int i = 0;
                bool skipOne = false;
                for (; i < _cards.Count; i++)
                {
                    var card = _cards[i];
                    if (card == _interactingCard)
                    {
                        skipOne = true;
                        continue;
                    }
                    if (right <= card.Position.X + card.Width)
                    {
                        break;
                    }
                }
                if (skipOne) i--;
                return i;
            }
        }

        void card_OnDragEndUnlock()
        {
            Trace.TraceInformation("DragEnd");
            _cardInteraction = CardInteraction.None;
            int newPos = _ComputeDragCardNewIndex();
            int oldPos = _cards.IndexOf(_interactingCard);
            if (newPos != oldPos)
            {
                _cards.Remove(_interactingCard);
                _cards.Insert(newPos, _interactingCard);
                var handler = OnHandCardMoved;
                if (handler != null)
                {
                    handler(oldPos, newPos);
                }
            }
            RearrangeCards(0.2d);
        }

        public event HandCardMovedHandler OnHandCardMoved;
        void card_OnDragEnd(object sender, EventArgs e)
        {
            if (_cardInteraction == CardInteraction.Drag)
            {
                lock (_cards)
                {
                    card_OnDragEndUnlock();
                }
                _cardInteraction = CardInteraction.MouseMove;
            }
        }

        void card_OnDragging(object sender, EventArgs e)
        {
            if (_cardInteraction == CardInteraction.Drag)
            {
                lock (_cards)
                {
                    RearrangeCards(0.2d);
                }
            }
        }

        void card_OnDragBegin(object sender, EventArgs e)
        {
            if (_cardInteraction == CardInteraction.MouseMove)
            {
                lock (_cards)
                {
                    Trace.TraceInformation("DragBegin");
                    _interactingCard = sender as CardView;
                    _interactingCard.SetValue(Canvas.ZIndexProperty, 1000);
                    Trace.Assert(_interactingCard != null);
                    _cardInteraction = CardInteraction.Drag;
                    RearrangeCards(0.2d);
                }
            }
        }
        #endregion

        #region Fields

        public GameView ParentGameView { get; set; }

        public int MaxCardSpacing { get; set; }

        public HorizontalAlignment CardAlignment { get; set; }

        public bool IsCardConsumer { get; set; }

        public int CardCapacity { get; set; }

        public bool KeepHorizontalOrder { get; set; }

        private List<CardView> _cards;
        public IList<CardView> Cards
        {
            get { return _cards; }
        }
        #endregion
    }
}
