// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore.ModelBuilding
{
    public class ModelBuilderNonGenericStringTest : ModelBuilderNonGenericTest
    {
        public class NonGenericStringOwnedTypes : OwnedTypesTestBase
        {
            [Fact]
            public virtual void Can_create_two_shadow_weak_owned_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;

                modelBuilder.Entity("Customer", b =>
                {
                    b.Property<string>("CustomerId");

                    b.HasKey("CustomerId");

                    b.OwnsOne("CustomerDetails", "Details", b1 =>
                    {
                        b1.Property<string>("CustomerId");

                        b1.HasKey("CustomerId");

                        b1.HasOne("Customer")
                            .WithOne("Details")
                            .HasForeignKey("CustomerDetails", "CustomerId")
                            .OnDelete(DeleteBehavior.Cascade);
                    });

                    b.OwnsOne("CustomerDetails", "AdditionalDetails", b1 =>
                    {
                        b1.Property<string>("CustomerId");

                        b1.HasKey("CustomerId");

                        b1.HasOne("Customer")
                            .WithOne("AdditionalDetails")
                            .HasForeignKey("CustomerDetails", "CustomerId")
                            .OnDelete(DeleteBehavior.Cascade);
                    });
                });

                var model = modelBuilder.Model;
                var ownership1 = model.FindEntityType("Customer").FindNavigation("Details").ForeignKey;
                var ownership2 = model.FindEntityType("Customer").FindNavigation("AdditionalDetails").ForeignKey;
                Assert.True(ownership1.IsRequired);
                Assert.True(ownership2.IsRequired);
                Assert.NotEqual(ownership1.DeclaringEntityType, ownership2.DeclaringEntityType);
                Assert.Equal(ownership1.Properties.Single().Name, ownership2.Properties.Single().Name);
                Assert.Equal(ownership1.DeclaringEntityType.FindPrimaryKey().Properties.Single().Name,
                    ownership2.DeclaringEntityType.FindPrimaryKey().Properties.Single().Name);
                Assert.Equal(2, model.GetEntityTypes().Count(e => e.Name == "CustomerDetails"));
            }

            protected override TestModelBuilder CreateTestModelBuilder(TestHelpers testHelpers)
                => new NonGenericStringTestModelBuilder(testHelpers);

            public override void Can_configure_one_to_one_relationship_from_an_owned_type()
            {
                var modelBuilder = CreateModelBuilder();

                // Test issue: HasOne<SpecialCustomer> in the base test is adding a shadow entity type when strings are
                // used. This would not normally happen, but it happens here because no navigation property
                // or type to do otherwise.
                modelBuilder.Entity<SpecialCustomer>();

                Can_configure_one_to_one_relationship_from_an_owned_type(modelBuilder);
            }

            public override void Weak_types_with_FK_to_another_entity_works()
            {
                var modelBuilder = CreateModelBuilder();

                // Test issue: HasOne<Country> in the base test is adding a shadow entity type when strings are
                // used. This would not normally happen, but it happens here because no navigation property
                // or type to do otherwise.
                modelBuilder.Entity<Country>();

                Weak_types_with_FK_to_another_entity_works(modelBuilder);
            }
        }

        public class NonGenericStringOneToManyType : OneToManyTestBase
        {
            [Fact]
            public virtual void Can_create_shadow_navigations_between_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var foreignKey = modelBuilder.Entity("Order")
                    .HasOne("Customer", "Customer")
                    .WithMany("Orders")
                    .Metadata;

                Assert.Equal("Customer", modelBuilder.Model.FindEntityType("Order")?.GetNavigations().Single().Name);
                Assert.Equal("Orders", modelBuilder.Model.FindEntityType("Customer")?.GetNavigations().Single().Name);
                Assert.False(foreignKey.IsUnique);

                Assert.Equal(
                    CoreStrings.ShadowEntity("Customer"),
                    Assert.Throws<InvalidOperationException>(() => modelBuilder.FinalizeModel()).Message);
            }

            [Fact]
            public virtual void Cannot_create_navigation_on_non_shadow_entity_targeting_shadow_entity()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var orderEntityType = modelBuilder.Entity(typeof(Order));

                Assert.Equal(
                    CoreStrings.NavigationToShadowEntity("Customer", typeof(Order).ShortDisplayName(), "Customer"),
                    Assert.Throws<InvalidOperationException>(() => orderEntityType.HasOne("Customer", "Customer")).Message);
            }

            [Fact]
            public virtual void Cannot_create_shadow_navigation_between_non_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var orderEntityType = modelBuilder.Entity(typeof(Order));

                Assert.Equal(
                    CoreStrings.NoClrNavigation("CustomerNavigation", typeof(Order).ShortDisplayName()),
                    Assert.Throws<InvalidOperationException>(() => orderEntityType.HasOne(typeof(Customer), "CustomerNavigation")).Message);
            }

            protected override TestModelBuilder CreateTestModelBuilder(TestHelpers testHelpers)
                => new NonGenericStringTestModelBuilder(testHelpers);
        }

        public class NonGenericStringManyToOneType : ManyToOneTestBase
        {
            [Fact]
            public virtual void Can_create_shadow_navigations_between_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var foreignKey = modelBuilder.Entity("Customer")
                    .HasMany("Order", "Orders")
                    .WithOne("Customer")
                    .Metadata;

                Assert.Equal("Customer", modelBuilder.Model.FindEntityType("Order")?.GetNavigations().Single().Name);
                Assert.Equal("Orders", modelBuilder.Model.FindEntityType("Customer")?.GetNavigations().Single().Name);
                Assert.False(foreignKey.IsUnique);

                Assert.Equal(
                    CoreStrings.ShadowEntity("Customer"),
                    Assert.Throws<InvalidOperationException>(() => modelBuilder.FinalizeModel()).Message);
            }

            [Fact]
            public virtual void Cannot_create_navigation_on_non_shadow_entity_targeting_shadow_entity()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var customerEntityType = modelBuilder.Entity(typeof(Customer));

                Assert.Equal(
                    CoreStrings.NavigationToShadowEntity("Orders", typeof(Customer).ShortDisplayName(), "Order"),
                    Assert.Throws<InvalidOperationException>(() => customerEntityType.HasMany("Order", "Orders")).Message);
            }

            [Fact]
            public virtual void Cannot_create_shadow_navigation_between_non_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var customerEntityType = modelBuilder.Entity(typeof(Customer));

                Assert.Equal(
                    CoreStrings.NoClrNavigation("OrdersNavigation", typeof(Customer).ShortDisplayName()),
                    Assert.Throws<InvalidOperationException>(() => customerEntityType.HasMany(typeof(Order), "OrdersNavigation")).Message);
            }

            protected override TestModelBuilder CreateTestModelBuilder(TestHelpers testHelpers)
                => new NonGenericStringTestModelBuilder(testHelpers);
        }

        public class NonGenericStringOneToOneType : OneToOneTestBase
        {
            [Fact]
            public virtual void Can_create_shadow_navigations_between_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var foreignKey = modelBuilder.Entity("Order")
                    .HasOne("OrderDetails", "OrderDetails")
                    .WithOne("Order")
                    .HasForeignKey("OrderDetails", "OrderId")
                    .Metadata;

                Assert.Equal("OrderDetails", modelBuilder.Model.FindEntityType("Order")?.GetNavigations().Single().Name);
                Assert.Equal("Order", modelBuilder.Model.FindEntityType("OrderDetails")?.GetNavigations().Single().Name);
                Assert.True(foreignKey.IsUnique);

                Assert.Equal(
                    CoreStrings.ShadowEntity("Order"),
                    Assert.Throws<InvalidOperationException>(() => modelBuilder.FinalizeModel()).Message);
            }

            [Fact]
            public virtual void Cannot_create_navigation_on_non_shadow_entity_targeting_shadow_entity()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var orderEntityType = modelBuilder.Entity(typeof(Order));

                Assert.Equal(
                    CoreStrings.NavigationToShadowEntity(nameof(Order.Details), typeof(Order).ShortDisplayName(), nameof(OrderDetails)),
                    Assert.Throws<InvalidOperationException>(() => orderEntityType.HasOne(nameof(OrderDetails), nameof(Order.Details))).Message);
            }

            [Fact]
            public virtual void Cannot_create_shadow_navigation_between_non_shadow_entity_types()
            {
                var modelBuilder = CreateModelBuilder().ModelBuilder;
                var orderEntityType = modelBuilder.Entity(typeof(Order));

                Assert.Equal(
                    CoreStrings.NoClrNavigation("OrderDetailsNavigation", typeof(Order).ShortDisplayName()),
                    Assert.Throws<InvalidOperationException>(() => orderEntityType.HasOne(typeof(OrderDetails), "OrderDetailsNavigation")).Message);
            }

            //Shadow navigations not supported #3864
            public override void Ignoring_properties_resolves_ambiguity()
            {
            }

            public override void Ignoring_properties_on_principal_resolves_ambiguity()
            {
            }

            public override void Throws_for_one_to_one_relationship_if_no_side_has_matching_property_anymore()
            {
            }

            protected override TestModelBuilder CreateTestModelBuilder(TestHelpers testHelpers)
                => new NonGenericStringTestModelBuilder(testHelpers);
        }

        private class NonGenericStringTestModelBuilder : TestModelBuilder
        {
            public NonGenericStringTestModelBuilder(TestHelpers testHelpers)
                : base(testHelpers)
            {
            }

            public override TestEntityTypeBuilder<TEntity> Entity<TEntity>()
                => new NonGenericStringTestEntityTypeBuilder<TEntity>(ModelBuilder.Entity(typeof(TEntity)));

            public override TestModelBuilder Entity<TEntity>(Action<TestEntityTypeBuilder<TEntity>> buildAction)
            {
                ModelBuilder.Entity(
                    typeof(TEntity),
                    e => buildAction(new NonGenericStringTestEntityTypeBuilder<TEntity>(e)));
                return this;
            }

            public override TestOwnedEntityTypeBuilder<TEntity> Owned<TEntity>()
                => new NonGenericTestOwnedEntityTypeBuilder<TEntity>(ModelBuilder.Owned(typeof(TEntity)));

            public override TestQueryTypeBuilder<TQuery> Query<TQuery>()
                => new NonGenericStringTestQueryTypeBuilder<TQuery>(ModelBuilder.Query(typeof(TQuery)));

            public override TestModelBuilder Query<TQuery>(Action<TestQueryTypeBuilder<TQuery>> buildAction)
            {
                ModelBuilder.Query(
                    typeof(TQuery), queryTypeBuilder => buildAction(new NonGenericStringTestQueryTypeBuilder<TQuery>(queryTypeBuilder)));
                return this;
            }

            public override TestModelBuilder Ignore<TEntity>()
            {
                ModelBuilder.Ignore(typeof(TEntity));
                return this;
            }

            public override string GetDisplayName(Type entityType) => entityType.FullName;
        }

        private class NonGenericStringTestEntityTypeBuilder<TEntity> : NonGenericTestEntityTypeBuilder<TEntity>
            where TEntity : class
        {
            public NonGenericStringTestEntityTypeBuilder(EntityTypeBuilder entityTypeBuilder)
                : base(entityTypeBuilder)
            {
            }

            protected override NonGenericTestEntityTypeBuilder<TEntity> Wrap(EntityTypeBuilder entityTypeBuilder)
                => new NonGenericStringTestEntityTypeBuilder<TEntity>(entityTypeBuilder);

            public override TestReferenceOwnershipBuilder<TEntity, TRelatedEntity> OwnsOne<TRelatedEntity>(
                Expression<Func<TEntity, TRelatedEntity>> navigationExpression)
                => new NonGenericStringTestReferenceOwnershipBuilder<TEntity, TRelatedEntity>(
                    EntityTypeBuilder.OwnsOne(typeof(TRelatedEntity).FullName, navigationExpression.GetPropertyAccess().GetSimpleMemberName()));

            public override TestEntityTypeBuilder<TEntity> OwnsOne<TRelatedEntity>(
                Expression<Func<TEntity, TRelatedEntity>> navigationExpression,
                Action<TestReferenceOwnershipBuilder<TEntity, TRelatedEntity>> buildAction)
                => Wrap(
                    EntityTypeBuilder.OwnsOne(
                        typeof(TRelatedEntity).FullName,
                        navigationExpression.GetPropertyAccess().GetSimpleMemberName(),
                        r => buildAction(new NonGenericStringTestReferenceOwnershipBuilder<TEntity, TRelatedEntity>(r))));

            public override TestReferenceNavigationBuilder<TEntity, TRelatedEntity> HasOne<TRelatedEntity>(
                Expression<Func<TEntity, TRelatedEntity>> navigationExpression = null)
                => new NonGenericStringTestReferenceNavigationBuilder<TEntity, TRelatedEntity>(
                    EntityTypeBuilder.HasOne(typeof(TRelatedEntity).FullName, navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));

            public override TestCollectionNavigationBuilder<TEntity, TRelatedEntity> HasMany<TRelatedEntity>(
                Expression<Func<TEntity, IEnumerable<TRelatedEntity>>> navigationExpression = null)
                => new NonGenericTestCollectionNavigationBuilder<TEntity, TRelatedEntity>(
                    EntityTypeBuilder.HasMany(typeof(TRelatedEntity).FullName, navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));
        }

        private class NonGenericStringTestQueryTypeBuilder<TQuery> : NonGenericTestQueryTypeBuilder<TQuery>
            where TQuery : class
        {
            public NonGenericStringTestQueryTypeBuilder(QueryTypeBuilder queryTypeBuilder)
                : base(queryTypeBuilder)
            {
            }

            protected override NonGenericTestQueryTypeBuilder<TQuery> Wrap(QueryTypeBuilder queryTypeBuilder)
                => new NonGenericStringTestQueryTypeBuilder<TQuery>(queryTypeBuilder);

            public override TestReferenceNavigationBuilder<TQuery, TRelatedEntity> HasOne<TRelatedEntity>(
                Expression<Func<TQuery, TRelatedEntity>> navigationExpression = null)
                => new NonGenericStringTestReferenceNavigationBuilder<TQuery, TRelatedEntity>(
                    QueryTypeBuilder.HasOne(typeof(TRelatedEntity).FullName, navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));
        }

        private class NonGenericStringTestReferenceNavigationBuilder<TEntity, TRelatedEntity>
            : NonGenericTestReferenceNavigationBuilder<TEntity, TRelatedEntity>
            where TEntity : class
            where TRelatedEntity : class
        {
            public NonGenericStringTestReferenceNavigationBuilder(ReferenceNavigationBuilder referenceNavigationBuilder)
                : base(referenceNavigationBuilder)
            {
            }

            public override TestReferenceReferenceBuilder<TEntity, TRelatedEntity> WithOne(
                Expression<Func<TRelatedEntity, TEntity>> navigationExpression = null)
                => new NonGenericStringTestReferenceReferenceBuilder<TEntity, TRelatedEntity>(
                    ReferenceNavigationBuilder.WithOne(navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));
        }

        private class NonGenericStringTestReferenceReferenceBuilder<TEntity, TRelatedEntity>
            : NonGenericTestReferenceReferenceBuilder<TEntity, TRelatedEntity>
            where TEntity : class
            where TRelatedEntity : class
        {
            public NonGenericStringTestReferenceReferenceBuilder(ReferenceReferenceBuilder referenceReferenceBuilder)
                : base(referenceReferenceBuilder)
            {
            }

            protected override NonGenericTestReferenceReferenceBuilder<TEntity, TRelatedEntity> Wrap(ReferenceReferenceBuilder referenceReferenceBuilder)
                => new NonGenericStringTestReferenceReferenceBuilder<TEntity, TRelatedEntity>(referenceReferenceBuilder);

            public override TestReferenceReferenceBuilder<TEntity, TRelatedEntity> HasForeignKey<TDependentEntity>(
                Expression<Func<TDependentEntity, object>> foreignKeyExpression)
                => Wrap(
                    ReferenceReferenceBuilder.HasForeignKey(
                        typeof(TDependentEntity).FullName, foreignKeyExpression.GetPropertyAccessList().Select(p => p.GetSimpleMemberName()).ToArray()));

            public override TestReferenceReferenceBuilder<TEntity, TRelatedEntity> HasPrincipalKey<TPrincipalEntity>(
                Expression<Func<TPrincipalEntity, object>> keyExpression)
                => Wrap(
                    ReferenceReferenceBuilder.HasPrincipalKey(
                        typeof(TPrincipalEntity).FullName, keyExpression.GetPropertyAccessList().Select(p => p.GetSimpleMemberName()).ToArray()));

            public override TestReferenceReferenceBuilder<TEntity, TRelatedEntity> HasForeignKey<TDependentEntity>(
                params string[] foreignKeyPropertyNames)
                => Wrap(ReferenceReferenceBuilder.HasForeignKey(typeof(TDependentEntity).FullName, foreignKeyPropertyNames));

            public override TestReferenceReferenceBuilder<TEntity, TRelatedEntity> HasPrincipalKey<TPrincipalEntity>(
                params string[] keyPropertyNames)
                => Wrap(ReferenceReferenceBuilder.HasPrincipalKey(typeof(TPrincipalEntity).FullName, keyPropertyNames));
        }

        private class NonGenericStringTestReferenceReferenceBuilder
        {
            public NonGenericStringTestReferenceReferenceBuilder(ReferenceReferenceBuilder referenceReferenceBuilder)
            {
                ReferenceReferenceBuilder = referenceReferenceBuilder;
            }

            public NonGenericStringTestReferenceReferenceBuilder HasForeignKey(
                string dependentEntityTypeName, params string[] foreignKeyPropertyNames)
                => new NonGenericStringTestReferenceReferenceBuilder(
                    ReferenceReferenceBuilder.HasForeignKey(dependentEntityTypeName, foreignKeyPropertyNames));

            public NonGenericStringTestReferenceReferenceBuilder HasPrincipalKey(
                string principalEntityTypeName, params string[] keyPropertyNames)
                => new NonGenericStringTestReferenceReferenceBuilder
                    (ReferenceReferenceBuilder.HasPrincipalKey(principalEntityTypeName, keyPropertyNames));

            private ReferenceReferenceBuilder ReferenceReferenceBuilder { get; }
            public IMutableForeignKey Metadata => ReferenceReferenceBuilder.Metadata;
        }

        private class NonGenericStringTestReferenceCollectionBuilder
        {
            public NonGenericStringTestReferenceCollectionBuilder(ReferenceCollectionBuilder referenceCollectionBuilder)
            {
                ReferenceCollectionBuilder = referenceCollectionBuilder;
            }

            private ReferenceCollectionBuilder ReferenceCollectionBuilder { get; }
            public IMutableForeignKey Metadata => ReferenceCollectionBuilder.Metadata;
        }

        private class NonGenericStringTestReferenceOwnershipBuilder<TEntity, TRelatedEntity>
            : NonGenericTestReferenceOwnershipBuilder<TEntity, TRelatedEntity>
            where TEntity : class
            where TRelatedEntity : class
        {
            public NonGenericStringTestReferenceOwnershipBuilder(ReferenceOwnershipBuilder referenceOwnershipBuilder)
                : base(referenceOwnershipBuilder)
            {
            }

            protected override NonGenericTestReferenceOwnershipBuilder<TNewEntity, TNewRelatedEntity> Wrap<TNewEntity, TNewRelatedEntity>(
                ReferenceOwnershipBuilder referenceOwnershipBuilder)
                => new NonGenericStringTestReferenceOwnershipBuilder<TNewEntity, TNewRelatedEntity>(referenceOwnershipBuilder);

            public override TestReferenceOwnershipBuilder<TRelatedEntity, TNewRelatedEntity> OwnsOne<TNewRelatedEntity>(
                Expression<Func<TRelatedEntity, TNewRelatedEntity>> navigationExpression)
                => new NonGenericStringTestReferenceOwnershipBuilder<TRelatedEntity, TNewRelatedEntity>(
                    ReferenceOwnershipBuilder.OwnsOne(typeof(TNewRelatedEntity).FullName, navigationExpression.GetPropertyAccess().GetSimpleMemberName()));

            public override TestReferenceOwnershipBuilder<TEntity, TRelatedEntity> OwnsOne<TNewRelatedEntity>(
                Expression<Func<TRelatedEntity, TNewRelatedEntity>> navigationExpression,
                Action<TestReferenceOwnershipBuilder<TRelatedEntity, TNewRelatedEntity>> buildAction)
                => Wrap<TEntity, TRelatedEntity>(
                    ReferenceOwnershipBuilder.OwnsOne(
                        typeof(TNewRelatedEntity).FullName,
                        navigationExpression.GetPropertyAccess().GetSimpleMemberName(),
                        r => buildAction(new NonGenericStringTestReferenceOwnershipBuilder<TRelatedEntity, TNewRelatedEntity>(r))));

            public override TestReferenceNavigationBuilder<TRelatedEntity, TNewRelatedEntity> HasOne<TNewRelatedEntity>(
                Expression<Func<TRelatedEntity, TNewRelatedEntity>> navigationExpression = null)
                => new NonGenericStringTestReferenceNavigationBuilder<TRelatedEntity, TNewRelatedEntity>(
                    ReferenceOwnershipBuilder.HasOne(typeof(TNewRelatedEntity).FullName, navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));

            public override TestCollectionNavigationBuilder<TRelatedEntity, TNewRelatedEntity> HasMany<TNewRelatedEntity>(
                Expression<Func<TRelatedEntity, IEnumerable<TNewRelatedEntity>>> navigationExpression = null)
                => new NonGenericTestCollectionNavigationBuilder<TRelatedEntity, TNewRelatedEntity>(
                    ReferenceOwnershipBuilder.HasMany(typeof(TNewRelatedEntity).FullName, navigationExpression?.GetPropertyAccess().GetSimpleMemberName()));
        }
    }
}
