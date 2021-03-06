﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sanguosha.Core.Skills;
using Sanguosha.Core.Players;

namespace Sanguosha.Core.Heroes
{
    public enum Allegiance
    {
        Unknown,
        Shu,
        Wei,
        Wu,
        Qun,
        God
    }

    public class Hero : ICloneable
    {
        public Allegiance Allegiance { get; set; }

        public List<ISkill> Skills { get; set; }

        public Player Owner { get; set; }

        public bool IsMale { get; set; }

        public int MaxHealth { get; set; }

        public Hero(string name, bool isMale, Allegiance a, int health, List<ISkill> skills)
        {
            Allegiance = a;
            Skills = skills;
            Name = name;
            MaxHealth = health;
            IsMale = isMale;
        }
        public Hero(string name, bool isMale, Allegiance a, int health, params ISkill[] skills)
        {
            Allegiance = a;
            Skills = new List<ISkill>(skills);
            Name = name;
            MaxHealth = health;
            IsMale = isMale;
        }

        public string Name { get; set; }

        public object Clone()
        {
            var hero = new Hero(Name, IsMale, Allegiance, MaxHealth, new List<ISkill>());
            hero.Skills = new List<ISkill>();
            foreach (var s in Skills)
            {
                hero.Skills.Add(s.Clone() as ISkill);
            }
            return hero;
        }
    }
}
