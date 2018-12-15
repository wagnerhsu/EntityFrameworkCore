﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query
{
    public abstract class GearsOfWarQueryFixtureBase : SharedStoreFixtureBase<GearsOfWarContext>, IQueryFixtureBase
    {
        protected override string StoreName { get; } = "GearsOfWarQueryTest";

        protected GearsOfWarQueryFixtureBase()
        {
            var entitySorters = new Dictionary<Type, Func<dynamic, object>>
            {
                { typeof(City), e => e?.Name },
                { typeof(CogTag), e => e?.Id },
                { typeof(Faction), e => e?.Id },
                { typeof(LocustHorde), e => e?.Id },
                { typeof(Gear), e => e?.SquadId + " " + e?.Nickname },
                { typeof(Officer), e => e?.SquadId + " " + e?.Nickname },
                { typeof(LocustLeader), e => e?.Name },
                { typeof(LocustCommander), e => e?.Name },
                { typeof(Mission), e => e?.Id },
                { typeof(Squad), e => e?.Id },
                { typeof(SquadMission), e => e?.SquadId + " " + e?.MissionId },
                { typeof(Weapon), e => e?.Id },
                { typeof(LocustHighCommand), e => e?.Id }
            };

            var entityAsserters = new Dictionary<Type, Action<dynamic, dynamic>>
            {
                {
                    typeof(City),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.Location, a.Location);
                            Assert.Equal(e[City.NationPropertyName], a[City.NationPropertyName]);
                        }
                    }
                },
                {
                    typeof(CogTag),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.Note, a.Note);
                            Assert.Equal(e.GearNickName, a.GearNickName);
                            Assert.Equal(e.GearSquadId, a.GearSquadId);
                        }
                    }
                },
                {
                    typeof(Faction),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.CapitalName, a.CapitalName);
                        }
                    }
                },
                {
                    typeof(Gear),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Nickname, a.Nickname);
                            Assert.Equal(e.SquadId, a.SquadId);
                            Assert.Equal(e.CityOrBirthName, a.CityOrBirthName);
                            Assert.Equal(e.FullName, a.FullName);
                            Assert.Equal(e.HasSoulPatch, a.HasSoulPatch);
                            Assert.Equal(e.LeaderNickname, a.LeaderNickname);
                            Assert.Equal(e.LeaderSquadId, a.LeaderSquadId);
                            Assert.Equal(e.Rank, a.Rank);
                        }
                    }
                },
                {
                    typeof(LocustCommander),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.ThreatLevel, a.ThreatLevel);
                            Assert.Equal(e.DefeatedByNickname, a.DefeatedByNickname);
                            Assert.Equal(e.DefeatedBySquadId, a.DefeatedBySquadId);
                        }
                    }
                },
                {
                    typeof(LocustHorde),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.CapitalName, a.CapitalName);
                            Assert.Equal(e.CommanderName, a.CommanderName);
                            Assert.Equal(e.Eradicated, a.Eradicated);
                        }
                    }
                },
                {
                    typeof(LocustLeader),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.ThreatLevel, a.ThreatLevel);
                        }
                    }
                },
                {
                    typeof(Mission),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.CodeName, a.CodeName);
                            Assert.Equal(e.Rating, a.Rating);
                            Assert.Equal(e.Timeline, a.Timeline);
                        }
                    }
                },
                {
                    typeof(Officer),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Nickname, a.Nickname);
                            Assert.Equal(e.SquadId, a.SquadId);
                            Assert.Equal(e.CityOrBirthName, a.CityOrBirthName);
                            Assert.Equal(e.FullName, a.FullName);
                            Assert.Equal(e.HasSoulPatch, a.HasSoulPatch);
                            Assert.Equal(e.LeaderNickname, a.LeaderNickname);
                            Assert.Equal(e.LeaderSquadId, a.LeaderSquadId);
                            Assert.Equal(e.Rank, a.Rank);
                        }
                    }
                },
                {
                    typeof(Squad),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.Name, a.Name);
                        }
                    }
                },
                {
                    typeof(SquadMission),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.SquadId, a.SquadId);
                            Assert.Equal(e.MissionId, a.MissionId);
                        }
                    }
                },
                {
                    typeof(Weapon),
                    (e, a) =>
                    {
                        Assert.Equal(e == null, a == null);

                        if (a != null)
                        {
                            Assert.Equal(e.Id, a.Id);
                            Assert.Equal(e.IsAutomatic, a.IsAutomatic);
                            Assert.Equal(e.Name, a.Name);
                            Assert.Equal(e.OwnerFullName, a.OwnerFullName);
                            Assert.Equal(e.SynergyWithId, a.SynergyWithId);
                        }
                    }
                }
            };

            QueryAsserter = new QueryAsserter<GearsOfWarContext>(
                CreateContext,
                new GearsOfWarData(),
                entitySorters,
                entityAsserters);
        }

        public QueryAsserterBase QueryAsserter { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.Entity<City>().HasKey(c => c.Name);
            modelBuilder.Entity<City>(
                    city => city.Metadata.AddIndexedProperty(City.NationPropertyName, typeof(string)));

            modelBuilder.Entity<Gear>(
                b =>
                {
                    b.HasKey(
                        g => new
                        {
                            g.Nickname,
                            g.SquadId
                        });

                    b.HasOne(g => g.CityOfBirth).WithMany(c => c.BornGears).HasForeignKey(g => g.CityOrBirthName).IsRequired();
                    b.HasOne(g => g.Tag).WithOne(t => t.Gear).HasForeignKey<CogTag>(
                        t => new
                        {
                            t.GearNickName,
                            t.GearSquadId
                        });
                    b.HasOne(g => g.AssignedCity).WithMany(c => c.StationedGears).IsRequired(false);
                });

            modelBuilder.Entity<Officer>().HasMany(o => o.Reports).WithOne().HasForeignKey(
                o => new
                {
                    o.LeaderNickname,
                    o.LeaderSquadId
                });

            modelBuilder.Entity<Squad>(
                b =>
                {
                    b.HasKey(s => s.Id);
                    b.Property(s => s.Id).ValueGeneratedNever();
                    b.HasMany(s => s.Members).WithOne(g => g.Squad).HasForeignKey(g => g.SquadId);
                });

            modelBuilder.Entity<Weapon>(
                b =>
                {
                    b.Property(w => w.Id).ValueGeneratedNever();
                    b.HasOne(w => w.SynergyWith).WithOne().HasForeignKey<Weapon>(w => w.SynergyWithId);
                    b.HasOne(w => w.Owner).WithMany(g => g.Weapons).HasForeignKey(w => w.OwnerFullName).HasPrincipalKey(g => g.FullName);
                });

            modelBuilder.Entity<Mission>().Property(m => m.Id).ValueGeneratedNever();

            modelBuilder.Entity<SquadMission>(
                b =>
                {
                    b.HasKey(
                        sm => new
                        {
                            sm.SquadId,
                            sm.MissionId
                        });
                    b.HasOne(sm => sm.Mission).WithMany(m => m.ParticipatingSquads).HasForeignKey(sm => sm.MissionId);
                    b.HasOne(sm => sm.Squad).WithMany(s => s.Missions).HasForeignKey(sm => sm.SquadId);
                });

            modelBuilder.Entity<Faction>().HasKey(f => f.Id);
            modelBuilder.Entity<Faction>().Property(f => f.Id).ValueGeneratedNever();

            modelBuilder.Entity<LocustHorde>().HasBaseType<Faction>();
            modelBuilder.Entity<LocustHorde>().HasMany(h => h.Leaders).WithOne();

            modelBuilder.Entity<LocustHorde>().HasOne(h => h.Commander).WithOne(c => c.CommandingFaction);

            modelBuilder.Entity<LocustLeader>().HasKey(l => l.Name);
            modelBuilder.Entity<LocustCommander>().HasBaseType<LocustLeader>();
            modelBuilder.Entity<LocustCommander>().HasOne(c => c.DefeatedBy).WithOne().HasForeignKey<LocustCommander>(
                c => new
                {
                    c.DefeatedByNickname,
                    c.DefeatedBySquadId
                });

            modelBuilder.Entity<LocustHighCommand>().HasKey(l => l.Id);
            modelBuilder.Entity<LocustHighCommand>().Property(l => l.Id).ValueGeneratedNever();
        }

        protected override void Seed(GearsOfWarContext context) => GearsOfWarContext.Seed(context);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(
                c => c.Log(CoreEventId.IncludeIgnoredWarning));

        public override GearsOfWarContext CreateContext()
        {
            var context = base.CreateContext();
            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return context;
        }
    }
}
