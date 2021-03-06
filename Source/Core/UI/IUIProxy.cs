﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sanguosha.Core.Cards;
using Sanguosha.Core.Players;
using Sanguosha.Core.Skills;
using Sanguosha.Core.Games;

namespace Sanguosha.Core.UI
{
    public enum VerifierResult
    {
        Success,
        Partial,
        Fail,
    }

    public class ResultDeckOptions
    {
        public string Name { get; set; }
        public int Maximum { get; set; }
        public bool Rearrangeable { get; set; }
        ResultDeckOptions()
        {
            Name = null;
            Maximum = 1;
            Rearrangeable = false;
        }
    }

    public class CardChoiceAddtionalOptions
    {
        List<ResultDeckOptions> ResultDecks { get; set; }
        int WindowId { get; set; }
        CardChoiceAddtionalOptions()
        {
            ResultDecks = new List<ResultDeckOptions>();
            WindowId = -1;
        }
    }
    
    public interface ICardChoiceVerifier
    {
        VerifierResult Verify(List<List<Card>> answer);
    }

    public delegate void CardChoiceRearrangeCallback(int fromDeck, int fromPlace, int toDeck, int toPlace);

    public interface IUiProxy
    {
        Player HostPlayer { get; set; }
        int TimeOutSeconds { get; set; }
        /// <summary>
        /// 询问使用或打出卡牌，可以发动技能。
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="verifier"></param>
        /// <param name="skill"></param>
        /// <param name="cards"></param>
        /// <param name="players"></param>
        /// <returns>False if user cannot provide an answer.</returns>
        bool AskForCardUsage(Prompt prompt, ICardUsageVerifier verifier,
                             out ISkill skill, out List<Card> cards, out List<Player> players);
        /// <summary>
        /// 询问用户从若干牌堆中选择卡牌，例如顺手牵羊，观星等等。
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="sourceDecks"></param>
        /// <param name="resultDeckNames"></param>
        /// <param name="resultDeckMaximums"></param>
        /// <param name="verifier"></param>
        /// <param name="answer">用户选择结果。对应resultDeckNames，每个选出的牌堆占用一个list。</param>
        /// <returns>False if user cannot provide an answer.</returns>
        bool AskForCardChoice(Prompt prompt, List<DeckPlace> sourceDecks, List<string> resultDeckNames,
                              List<int> resultDeckMaximums,
                              ICardChoiceVerifier verifier, out List<List<Card>> answer, List<bool> rearrangeable, ref int windowId, CardChoiceRearrangeCallback callback = null);

        /// <summary>
        /// 询问多选题目，例如是否发动洛神
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="questions">问题列表</param>
        /// <param name="answer">回答</param>
        /// <returns></returns>
        bool AskForMultipleChoice(Prompt prompt, List<string> questions, out int answer);

        void Freeze();
    }
}
