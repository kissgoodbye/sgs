﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sanguosha.Core.Triggers;
using Sanguosha.Core.Cards;
using Sanguosha.Core.UI;
using Sanguosha.Core.Skills;
using Sanguosha.Expansions.Battle.Cards;

namespace Sanguosha.Expansions.Fire.Skills
{
    /// <summary>
    /// 连环-出牌阶段，你可以将一张梅花牌当【铁索连环】使用。
    /// </summary>
    public class LianHuan : OneToOneCardTransformSkill
    {
        public override bool VerifyInput(Card card, object arg)
        {
            return card.Suit == SuitType.Club;
        }

        public override CardHandler PossibleResult
        {
            get { return new TieSuoLianHuan(); }
        }

        public override bool HandCardOnly
        {
            get
            {
                return true;
            }
        }
    }
}
