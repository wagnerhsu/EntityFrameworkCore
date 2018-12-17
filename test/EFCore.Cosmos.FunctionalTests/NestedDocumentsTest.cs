﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Cosmos.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Cosmos
{
    public class NestedDocumentsTest
    {
        public NestedDocumentsTest(ITestOutputHelper testOutputHelper)
        {
            TestSqlLoggerFactory = (TestSqlLoggerFactory)TestStoreFactory.CreateListLoggerFactory(_ => true);
            //TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        // #13579
        // [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public virtual void Can_update_dependents(bool useNesting)
        {
            using (CreateTestStore(modelBuilder =>
                {
                    OnModelCreating(modelBuilder);

                    if (!useNesting)
                    {
                        RemoveNesting(modelBuilder);
                    }
                }))
            {
                Operator firstOperator;
                Engine firstEngine;
                using (var context = CreateContext())
                {
                    firstOperator = context.Set<Vehicle>().Select(v => v.Operator).OrderBy(o => o.VehicleName).First();
                    firstOperator.Name += "1";
                    firstEngine = context.Set<PoweredVehicle>().Select(v => v.Engine).OrderBy(o => o.VehicleName).First();
                    firstEngine.Description += "1";

                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    Assert.Equal(firstOperator.Name,
                        context.Set<Vehicle>().Select(v => v.Operator).OrderBy(o => o.VehicleName).First().Name);
                    Assert.Equal(firstEngine.Description,
                        context.Set<PoweredVehicle>().Select(v => v.Engine).OrderBy(o => o.VehicleName).First().Description);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public virtual void Can_update_owner_with_dependents(bool useNesting)
        {
            using (CreateTestStore(modelBuilder =>
                {
                    OnModelCreating(modelBuilder);

                    if (!useNesting)
                    {
                        RemoveNesting(modelBuilder);
                    }
                }))
            {
                Operator firstOperator;
                Engine firstEngine;
                using (var context = CreateContext())
                {
                    firstOperator = context.Set<Vehicle>().OrderBy(o => o.Operator.VehicleName).First().Operator;
                    firstOperator.Name += "1";
                    firstEngine = context.Set<PoweredVehicle>().OrderBy(o => o.Engine.VehicleName).First().Engine;
                    firstEngine.Description += "1";

                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    Assert.Equal(firstOperator.Name,
                        context.Set<Vehicle>().OrderBy(o => o.Operator.VehicleName).First().Operator.Name);
                    Assert.Equal(firstEngine.Description,
                        context.Set<PoweredVehicle>().OrderBy(o => o.Engine.VehicleName).First().Engine.Description);
                }
            }
        }

        // #13559
        //[Theory]
        [InlineData(true)]
        [InlineData(false)]
        public virtual void Can_update_just_dependents(bool useNesting)
        {
            using (CreateTestStore(modelBuilder =>
                {
                    OnModelCreating(modelBuilder);

                    if (!useNesting)
                    {
                        RemoveNesting(modelBuilder);
                    }
                }))
            {
                Operator firstOperator;
                Engine firstEngine;
                using (var context = CreateContext())
                {
                    firstOperator = context.Set<Operator>().OrderBy(o => o.VehicleName).First();
                    firstOperator.Name += "1";
                    firstEngine = context.Set<Engine>().OrderBy(o => o.VehicleName).First();
                    firstEngine.Description += "1";

                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    Assert.Equal(firstOperator.Name, context.Set<Operator>().OrderBy(o => o.VehicleName).First().Name);
                    Assert.Equal(firstEngine.Description, context.Set<Engine>().OrderBy(o => o.VehicleName).First().Description);
                }
            }
        }

        [Fact]
        public virtual void Quering_nested_entity_directly_throws()
        {
            using (CreateTestStore(OnModelCreating))
            {
                using (var context = CreateContext())
                {
                    Assert.Equal(CosmosStrings.QueryRootNestedEntityType(nameof(Operator), nameof(Vehicle)),
                        Assert.Throws<InvalidOperationException>(() => context.Set<Operator>().ToList()).Message);
                }
            }
        }

        // #13559
        //[Fact]
        public virtual void Can_query_nested_derived_hierarchy()
        {
            using (CreateTestStore(OnModelCreating))
            {
                using (var context = CreateContext())
                {
                    Assert.Equal(2, context.Set<FuelTank>().ToList().Count);
                }
            }
        }

        // #13559
        //[Fact]
        public virtual void Can_query_nested_derived_nonhierarchy()
        {
            using (CreateTestStore(
                modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Ignore<SolidFuelTank>();
                }))
            {
                using (var context = CreateContext())
                {
                    Assert.Equal(2, context.Set<FuelTank>().ToList().Count);
                }
            }
        }

        [Fact]
        public virtual void Can_roundtrip()
        {
            Test_roundtrip(
                modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Entity<FuelTank>(eb => eb.Ignore(e => e.Vehicle));
                });
        }

        [Fact]
        public virtual void Can_roundtrip_with_redundant_relationships()
        {
            Test_roundtrip(OnModelCreating);
        }

        [Fact]
        public virtual void Can_roundtrip_with_fanned_relationships()
        {
            Test_roundtrip(
                modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Entity<SolidFuelTank>(eb => eb.Ignore(e => e.Rocket));
                    modelBuilder.Entity<SolidRocket>(eb => eb.Ignore(e => e.SolidFuelTank));
                });
        }

        protected void Test_roundtrip(Action<ModelBuilder> onModelCreating)
        {
            using (CreateTestStore(onModelCreating))
            {
                using (var context = CreateContext())
                {
                    context.AssertSeeded();
                }
            }
        }

        [Fact]
        public virtual void Inserting_dependent_without_principal_throws()
        {
            using (CreateTestStore(OnModelCreating))
            {
                using (var context = CreateContext())
                {
                    context.Add(
                        new PoweredVehicle
                        {
                            Name = "Fuel transport",
                            SeatingCapacity = 1,
                            Operator = new LicensedOperator
                            {
                                Name = "Jack Jackson",
                                LicenseType = "Class A CDC"
                            }
                        });
                    context.Add(
                        new FuelTank
                        {
                            Capacity = "10000 l",
                            FuelType = "Gas",
                            VehicleName = "Fuel transport"
                        });

                    Assert.Equal(
                        CosmosStrings.OrphanedNestedDocumentSensitive(
                            nameof(FuelTank), nameof(CombustionEngine), "{VehicleName: Fuel transport}"),
                        Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
                }
            }
        }

        [Fact]
        public virtual void Can_change_nested_instance_non_derived()
        {
            using (CreateTestStore(
                modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Entity<Engine>().ToContainer("TransportationContext");
                    modelBuilder.Entity<FuelTank>(
                        eb =>
                        {
                            eb.ToContainer("TransportationContext");
                            eb.HasOne(e => e.Engine)
                                .WithOne(e => e.FuelTank)
                                .HasForeignKey<FuelTank>(e => e.VehicleName)
                                .OnDelete(DeleteBehavior.Restrict);
                        });
                    modelBuilder.Ignore<SolidFuelTank>();
                    modelBuilder.Ignore<SolidRocket>();
                }))
            {
                using (var context = CreateContext())
                {
                    var bike = context.Vehicles.Single(v => v.Name == "Trek Pro Fit Madone 6 Series");

                    bike.Operator = new Operator
                    {
                        Name = "Chris Horner"
                    };

                    context.ChangeTracker.DetectChanges();

                    bike.Operator = new LicensedOperator
                    {
                        Name = "repairman",
                        LicenseType = "Repair"
                    };

                    TestSqlLoggerFactory.Clear();
                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    var bike = context.Vehicles.Single(v => v.Name == "Trek Pro Fit Madone 6 Series");
                    Assert.Equal("repairman", bike.Operator.Name);

                    Assert.Equal("Repair", ((LicensedOperator)bike.Operator).LicenseType);
                }
            }
        }

        [Fact]
        public virtual void Can_change_principal_instance_non_derived()
        {
            using (CreateTestStore(
                modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Entity<Engine>().ToContainer("TransportationContext");
                    modelBuilder.Entity<FuelTank>(
                        eb =>
                        {
                            eb.ToContainer("TransportationContext");
                            eb.HasOne(e => e.Engine)
                                .WithOne(e => e.FuelTank)
                                .HasForeignKey<FuelTank>(e => e.VehicleName)
                                .OnDelete(DeleteBehavior.Restrict);
                        });
                    modelBuilder.Ignore<SolidFuelTank>();
                    modelBuilder.Ignore<SolidRocket>();
                }))
            {
                using (var context = CreateContext())
                {
                    var bike = context.Vehicles.Single(v => v.Name == "Trek Pro Fit Madone 6 Series");

                    var newBike = new Vehicle
                    {
                        Name = "Trek Pro Fit Madone 6 Series",
                        Operator = bike.Operator,
                        SeatingCapacity = 2
                    };

                    var oldEntry = context.Remove(bike);
                    var newEntry = context.Add(newBike);

                    TestSqlLoggerFactory.Clear();
                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    var bike = context.Vehicles.Single(v => v.Name == "Trek Pro Fit Madone 6 Series");

                    Assert.Equal(2, bike.SeatingCapacity);
                    Assert.NotNull(bike.Operator);
                }
            }
        }

        protected readonly string DatabaseName = "NestedDocumentsTest";
        protected TestStore TestStore { get; set; }
        protected ITestStoreFactory TestStoreFactory => CosmosTestStoreFactory.Instance;
        protected IServiceProvider ServiceProvider { get; set; }
        protected TestSqlLoggerFactory TestSqlLoggerFactory { get; }

        protected void AssertSql(params string[] expected)
            => TestSqlLoggerFactory.AssertBaseline(expected);

        protected void AssertContainsSql(params string[] expected)
            => TestSqlLoggerFactory.AssertBaseline(expected, assertOrder: false);

        protected virtual void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Vehicle>(
                eb => eb.OwnsOne(v => v.Operator));

            modelBuilder.Entity<CombustionEngine>(
                eb => eb.OwnsOne(v => v.FuelTank));

            modelBuilder.Entity<PoweredVehicle>(
                eb => eb.OwnsOne(v => v.Engine));
        }

        private static void RemoveNesting(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Vehicle>(
                eb => eb.OwnsOne(v => v.Operator).ToContainer(nameof(TransportationContext)));

            modelBuilder.Entity<CombustionEngine>(
                eb => eb.OwnsOne(v => v.FuelTank).ToContainer(nameof(TransportationContext)));

            modelBuilder.Entity<PoweredVehicle>(
                eb => eb.OwnsOne(v => v.Engine).ToContainer(nameof(TransportationContext)));
        }

        protected TestStore CreateTestStore(Action<ModelBuilder> onModelCreating)
        {
            TestStore = TestStoreFactory.Create(DatabaseName);

            ServiceProvider = TestStoreFactory.AddProviderServices(new ServiceCollection())
                .AddSingleton(TestModelSource.GetFactory(onModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider(validateScopes: true);

            TestStore.Initialize(ServiceProvider, CreateContext, c => ((TransportationContext)c).Seed());

            TestSqlLoggerFactory.Clear();

            return TestStore;
        }

        protected virtual DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder
                .EnableSensitiveDataLogging()
                .ConfigureWarnings(
                    b => b.Default(WarningBehavior.Throw)
                        .Log(CoreEventId.SensitiveDataLoggingEnabledWarning)
                        .Log(CoreEventId.PossibleUnintendedReferenceComparisonWarning));

        protected virtual TransportationContext CreateContext()
        {
            var options = AddOptions(TestStore.AddProviderOptions(new DbContextOptionsBuilder()))
                .UseInternalServiceProvider(ServiceProvider).Options;
            return new TransportationContext(options);
        }
    }
}
