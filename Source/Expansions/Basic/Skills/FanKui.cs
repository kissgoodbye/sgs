﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

using Sanguosha.Core.Triggers;
using Sanguosha.Core.Cards;
using Sanguosha.Core.UI;
using Sanguosha.Core.Skills;
using Sanguosha.Expansions.Basic.Cards;
using Sanguosha.Core.Games;
using Sanguosha.Core.Players;
using Sanguosha.Core.Exceptions;

namespace Sanguosha.Expansions.Basic.Skills
{
    /// <summary>
    /// 反馈-每当你受到一次伤害后，你可以获得伤害来源的一张牌。
    /// </summary>
    public class FanKui : TriggerSkill
    {
        public void OnAfterDamageInflicted(Player owner, GameEvent gameEvent, GameEventArgs eventArgs)
        {
            NotifySkillUse(new List<Player>() { eventArgs.Source });
            List<DeckPlace> deck = new List<DeckPlace>();
            deck.Add(new DeckPlace(eventArgs.Source, DeckType.Hand));
            deck.Add(new DeckPlace(eventArgs.Source, DeckType.Equipment));
            List<int> max = new List<int>() { 1 };
            List<List<Card>> result;
            List<string> deckname = new List<string>() {"FanKui choice"};
            Card theCard;

            int windowId = 0;
            if (!Game.CurrentGame.UiProxies[Owner].AskForCardChoice(new CardChoicePrompt("FanKui", eventArgs.Source), deck, deckname, max, new RequireOneCardChoiceVerifier(), out result, new List<bool>() { false }, ref windowId))
            {

                Trace.TraceInformation("Invalid choice for FanKui");
                theCard = Game.CurrentGame.Decks[eventArgs.Source, DeckType.Hand]
                    .Concat(Game.CurrentGame.Decks[eventArgs.Source, DeckType.Equipment]).First();
            }
            else
            {
                theCard = result[0][0];
            }
            List<Card> cards = new List<Card>();
            cards.Add(theCard);
            Game.CurrentGame.HandleCardTransferToHand(eventArgs.Source, owner, cards);
        }

        public FanKui()
        {
            var trigger = new AutoNotifyPassiveSkillTrigger(
                this,
                OnAfterDamageInflicted,
                TriggerCondition.OwnerIsTarget | TriggerCondition.SourceHasCards
            ) { IsAutoNotify = false };
            Triggers.Add(GameEvent.AfterDamageInflicted, trigger);
            IsAutoInvoked = null;
        }

    }
}
