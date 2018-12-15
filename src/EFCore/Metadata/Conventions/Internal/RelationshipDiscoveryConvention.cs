// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class RelationshipDiscoveryConvention :
        IEntityTypeAddedConvention,
        IBaseTypeChangedConvention,
        INavigationRemovedConvention,
        IEntityTypeMemberIgnoredConvention,
        INavigationAddedConvention,
        IForeignKeyOwnershipChangedConvention
    {
        private readonly IMemberClassifier _memberClassifier;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Model> _logger;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public RelationshipDiscoveryConvention(
            [NotNull] IMemberClassifier memberClassifier,
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
        {
            Check.NotNull(memberClassifier, nameof(memberClassifier));
            Check.NotNull(logger, nameof(logger));

            _memberClassifier = memberClassifier;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public const string NavigationCandidatesAnnotationName = "RelationshipDiscoveryConvention:NavigationCandidates";

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public const string AmbiguousNavigationsAnnotationName = "RelationshipDiscoveryConvention:AmbiguousNavigations";

        private InternalEntityTypeBuilder DiscoverRelationships(InternalEntityTypeBuilder entityTypeBuilder)
        {
            if (!entityTypeBuilder.Metadata.HasClrType()
                || entityTypeBuilder.ModelBuilder.IsIgnored(entityTypeBuilder.Metadata.ClrType, ConfigurationSource.Convention))
            {
                return entityTypeBuilder;
            }

            var relationshipCandidates = FindRelationshipCandidates(entityTypeBuilder);
            relationshipCandidates = RemoveIncompatibleWithExistingRelationships(relationshipCandidates, entityTypeBuilder);
            relationshipCandidates = RemoveInheritedInverseNavigations(relationshipCandidates);
            relationshipCandidates = RemoveSingleSidedBaseNavigations(relationshipCandidates, entityTypeBuilder);

            using (entityTypeBuilder.Metadata.Model.ConventionDispatcher.StartBatch())
            {
                CreateRelationships(relationshipCandidates, entityTypeBuilder);
            }

            return entityTypeBuilder;
        }

        private IReadOnlyList<RelationshipCandidate> FindRelationshipCandidates(InternalEntityTypeBuilder entityTypeBuilder)
        {
            var entityType = entityTypeBuilder.Metadata;
            var model = entityType.Model;
            var relationshipCandidates = new Dictionary<EntityType, RelationshipCandidate>();
            var ownership = entityTypeBuilder.Metadata.FindOwnership();
            if (ownership == null
                && model.ShouldBeOwnedType(entityTypeBuilder.Metadata.ClrType))
            {
                return relationshipCandidates.Values.ToList();
            }

            foreach (var candidateTuple in GetNavigationCandidates(entityType))
            {
                var navigationPropertyInfo = candidateTuple.Key;
                var targetClrType = candidateTuple.Value;

                if (!IsCandidateNavigationProperty(entityTypeBuilder, navigationPropertyInfo.GetSimpleMemberName(), navigationPropertyInfo)
                    || (model.ShouldBeOwnedType(targetClrType)
                        && HasDeclaredAmbiguousNavigationsTo(entityType, targetClrType)))
                {
                    continue;
                }

                var candidateTargetEntityTypeBuilder = GetTargetEntityTypeBuilder(
                    entityTypeBuilder, targetClrType, navigationPropertyInfo, ownership, ConfigurationSource.Convention);

                if (candidateTargetEntityTypeBuilder == null)
                {
                    continue;
                }

                var candidateTargetEntityType = candidateTargetEntityTypeBuilder.Metadata;
                if (candidateTargetEntityType.IsQueryType)
                {
                    continue;
                }

                if (entityType.Builder == null)
                {
                    foreach (var relationshipCandidate in relationshipCandidates.Values)
                    {
                        var targetType = relationshipCandidate.TargetTypeBuilder.Metadata;
                        if (targetType.Builder != null
                            && targetType.HasDefiningNavigation()
                            && targetType.DefiningEntityType.FindNavigation(targetType.DefiningNavigationName) == null)
                        {
                            targetType.Builder.ModelBuilder.RemoveEntityType(targetType, ConfigurationSource.Convention);
                        }
                    }

                    return Array.Empty<RelationshipCandidate>();
                }

                if (!model.ShouldBeOwnedType(targetClrType))
                {
                    var targetOwnership = candidateTargetEntityType.FindOwnership();
                    if (targetOwnership != null
                        && (targetOwnership.PrincipalEntityType != entityType
                            || targetOwnership.PrincipalToDependent.Name != navigationPropertyInfo.GetSimpleMemberName())
                        && (ownership == null
                            || ownership.PrincipalEntityType != candidateTargetEntityType))
                    {
                        continue;
                    }
                }

                if (relationshipCandidates.TryGetValue(candidateTargetEntityType, out var existingCandidate))
                {
                    if (candidateTargetEntityType != entityType
                        || !existingCandidate.InverseProperties.Contains(navigationPropertyInfo))
                    {
                        if (!existingCandidate.NavigationProperties.Contains(navigationPropertyInfo))
                        {
                            existingCandidate.NavigationProperties.Add(navigationPropertyInfo);
                        }
                    }

                    continue;
                }

                var navigations = new List<PropertyInfo>
                {
                    navigationPropertyInfo
                };
                var inverseCandidates = GetNavigationCandidates(candidateTargetEntityType);
                var inverseNavigationCandidates = new List<PropertyInfo>();

                foreach (var inverseCandidateTuple in inverseCandidates)
                {
                    var inversePropertyInfo = inverseCandidateTuple.Key;
                    var inverseTargetType = inverseCandidateTuple.Value;

                    if ((inverseTargetType != entityType.ClrType
                         && (!inverseTargetType.IsAssignableFrom(entityType.ClrType)
                             || (!model.ShouldBeOwnedType(targetClrType)
                                 && !candidateTargetEntityType.IsInOwnershipPath(entityType))))
                        || navigationPropertyInfo.IsSameAs(inversePropertyInfo)
                        || entityType.IsQueryType
                        || (ownership != null
                            && !candidateTargetEntityType.IsInOwnershipPath(entityType)
                            && (candidateTargetEntityType.IsOwned()
                                || !model.ShouldBeOwnedType(targetClrType))
                            && (ownership.PrincipalEntityType != candidateTargetEntityType
                                || ownership.PrincipalToDependent.Name != inversePropertyInfo.GetSimpleMemberName()))
                        || (entityType.HasDefiningNavigation()
                            && !candidateTargetEntityType.IsInDefinitionPath(entityType.ClrType)
                            && (entityType.DefiningEntityType != candidateTargetEntityType
                                || entityType.DefiningNavigationName != inversePropertyInfo.GetSimpleMemberName()))
                        || !IsCandidateNavigationProperty(
                            candidateTargetEntityTypeBuilder, inversePropertyInfo.GetSimpleMemberName(), inversePropertyInfo))
                    {
                        continue;
                    }

                    if (!inverseNavigationCandidates.Contains(inversePropertyInfo))
                    {
                        inverseNavigationCandidates.Add(inversePropertyInfo);
                    }
                }

                relationshipCandidates[candidateTargetEntityType] =
                    new RelationshipCandidate(candidateTargetEntityTypeBuilder, navigations, inverseNavigationCandidates);
            }

            var candidates = new List<RelationshipCandidate>();
            foreach (var relationshipCandidate in relationshipCandidates.Values)
            {
                if (relationshipCandidate.TargetTypeBuilder.Metadata.Builder != null)
                {
                    candidates.Add(relationshipCandidate);
                    continue;
                }

                if (relationshipCandidate.NavigationProperties.Count > 1)
                {
                    continue;
                }

                // The entity type might have been converted to a weak entity type
                var actualTargetEntityTypeBuilder = GetTargetEntityTypeBuilder(
                    entityTypeBuilder,
                    relationshipCandidate.TargetTypeBuilder.Metadata.ClrType,
                    relationshipCandidate.NavigationProperties.Single(),
                    ownership,
                    ConfigurationSource.Convention);

                if (actualTargetEntityTypeBuilder == null)
                {
                    continue;
                }

                candidates.Add(new RelationshipCandidate(
                    actualTargetEntityTypeBuilder, relationshipCandidate.NavigationProperties, relationshipCandidate.InverseProperties));
            }

            return candidates;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static InternalEntityTypeBuilder GetTargetEntityTypeBuilder(
            InternalEntityTypeBuilder entityTypeBuilder,
            Type targetClrType,
            MemberInfo navigationInfo,
            ConfigurationSource? configurationSource)
            => GetTargetEntityTypeBuilder(entityTypeBuilder, targetClrType, navigationInfo,
                entityTypeBuilder.Metadata.FindOwnership(), configurationSource);

        private static InternalEntityTypeBuilder GetTargetEntityTypeBuilder(
            InternalEntityTypeBuilder entityTypeBuilder,
            Type targetClrType,
            MemberInfo navigationInfo,
            ForeignKey ownership,
            ConfigurationSource? configurationSource)
        {
            // ReSharper disable CheckForReferenceEqualityInstead.1
            // ReSharper disable CheckForReferenceEqualityInstead.3
            if (ownership != null)
            {
                if (targetClrType.Equals(entityTypeBuilder.Metadata.ClrType))
                {
                    return null;
                }

                if (targetClrType.IsAssignableFrom(ownership.PrincipalEntityType.ClrType))
                {
                    if (configurationSource != null)
                    {
                        ownership.PrincipalEntityType.UpdateConfigurationSource(configurationSource.Value);
                    }

                    return ownership.PrincipalEntityType.Builder;
                }
            }

            var entityType = entityTypeBuilder.Metadata;
            InternalEntityTypeBuilder targetEntityTypeBuilder = null;
            if (!entityTypeBuilder.ModelBuilder.Metadata.EntityTypeShouldHaveDefiningNavigation(targetClrType))
            {
                var targetEntityType = entityTypeBuilder.ModelBuilder.Metadata.FindEntityType(targetClrType);

                var existingOwnership = targetEntityType?.FindOwnership();
                if (existingOwnership != null
                    && entityType.Model.ShouldBeOwnedType(targetClrType)
                    && (existingOwnership.PrincipalEntityType != entityType
                        || existingOwnership.PrincipalToDependent.Name != navigationInfo.GetSimpleMemberName()))
                {
                    return configurationSource.HasValue
                           && !targetClrType.Equals(entityTypeBuilder.Metadata.ClrType)
                        ? entityTypeBuilder.ModelBuilder.Entity(
                            targetClrType, navigationInfo.GetSimpleMemberName(), entityType, configurationSource.Value)
                        : null;
                }

                targetEntityTypeBuilder = configurationSource.HasValue
                    ? entityTypeBuilder.ModelBuilder.Entity(targetClrType, configurationSource.Value, allowOwned: true)
                    : targetEntityType?.Builder;
            }
            else if (!targetClrType.Equals(entityTypeBuilder.Metadata.ClrType))
            {
                if (entityType.DefiningEntityType?.ClrType.Equals(targetClrType) == true)
                {
                    if (configurationSource != null)
                    {
                        entityType.DefiningEntityType.UpdateConfigurationSource(configurationSource.Value);
                    }

                    return entityType.DefiningEntityType.Builder;
                }

                targetEntityTypeBuilder =
                    entityType.FindNavigation(navigationInfo.GetSimpleMemberName())?.GetTargetType().Builder
                    ?? entityType.Model.FindEntityType(
                        targetClrType, navigationInfo.GetSimpleMemberName(), entityType)?.Builder;

                if (targetEntityTypeBuilder == null
                    && configurationSource.HasValue
                    && !entityType.IsInDefinitionPath(targetClrType)
                    && !entityType.IsInOwnershipPath(targetClrType))
                {
                    return entityTypeBuilder.ModelBuilder.Entity(
                        targetClrType, navigationInfo.GetSimpleMemberName(), entityType, configurationSource.Value);
                }

                if (configurationSource != null)
                {
                    targetEntityTypeBuilder?.Metadata.UpdateConfigurationSource(configurationSource.Value);
                }
            }
            // ReSharper restore CheckForReferenceEqualityInstead.1
            // ReSharper restore CheckForReferenceEqualityInstead.3

            return targetEntityTypeBuilder;
        }

        private static IReadOnlyList<RelationshipCandidate> RemoveIncompatibleWithExistingRelationships(
            IReadOnlyList<RelationshipCandidate> relationshipCandidates,
            InternalEntityTypeBuilder entityTypeBuilder)
        {
            if (relationshipCandidates.Count == 0)
            {
                return relationshipCandidates;
            }

            var entityType = entityTypeBuilder.Metadata;
            var filteredRelationshipCandidates = new List<RelationshipCandidate>();
            foreach (var relationshipCandidate in relationshipCandidates)
            {
                var targetEntityTypeBuilder = relationshipCandidate.TargetTypeBuilder;
                var targetEntityType = targetEntityTypeBuilder.Metadata;
                while (relationshipCandidate.NavigationProperties.Count > 0)
                {
                    var navigationProperty = relationshipCandidate.NavigationProperties[0];
                    var existingNavigation = entityType.FindNavigation(navigationProperty.GetSimpleMemberName());
                    if (existingNavigation != null
                        && (existingNavigation.DeclaringEntityType != entityType
                            || existingNavigation.GetTargetType() != targetEntityType))
                    {
                        relationshipCandidate.NavigationProperties.Remove(navigationProperty);
                        continue;
                    }

                    if (relationshipCandidate.NavigationProperties.Count == 1
                        && relationshipCandidate.InverseProperties.Count == 0)
                    {
                        break;
                    }

                    PropertyInfo compatibleInverse = null;
                    foreach (var inverseProperty in relationshipCandidate.InverseProperties)
                    {
                        if (IsCompatibleInverse(
                            navigationProperty, inverseProperty, entityTypeBuilder, targetEntityTypeBuilder))
                        {
                            if (compatibleInverse == null)
                            {
                                compatibleInverse = inverseProperty;
                            }
                            else
                            {
                                goto NextCandidate;
                            }
                        }
                    }

                    if (compatibleInverse == null)
                    {
                        relationshipCandidate.NavigationProperties.Remove(navigationProperty);

                        filteredRelationshipCandidates.Add(
                            new RelationshipCandidate(
                                targetEntityTypeBuilder,
                                new List<PropertyInfo>
                                {
                                    navigationProperty
                                },
                                new List<PropertyInfo>()));

                        if (relationshipCandidate.TargetTypeBuilder.Metadata == entityTypeBuilder.Metadata
                            && relationshipCandidate.InverseProperties.Count > 0)
                        {
                            var nextSelfRefCandidate = relationshipCandidate.InverseProperties.First();
                            if (!relationshipCandidate.NavigationProperties.Contains(nextSelfRefCandidate))
                            {
                                relationshipCandidate.NavigationProperties.Add(nextSelfRefCandidate);
                            }

                            relationshipCandidate.InverseProperties.Remove(nextSelfRefCandidate);
                        }

                        continue;
                    }

                    var noOtherCompatibleNavigation = true;
                    foreach (var n in relationshipCandidate.NavigationProperties)
                    {
                        if (n != navigationProperty
                            && IsCompatibleInverse(n, compatibleInverse, entityTypeBuilder, targetEntityTypeBuilder))
                        {
                            noOtherCompatibleNavigation = false;
                            break;
                        }
                    }

                    if (noOtherCompatibleNavigation)
                    {
                        relationshipCandidate.NavigationProperties.Remove(navigationProperty);
                        relationshipCandidate.InverseProperties.Remove(compatibleInverse);

                        filteredRelationshipCandidates.Add(
                            new RelationshipCandidate(
                                targetEntityTypeBuilder,
                                new List<PropertyInfo>
                                {
                                    navigationProperty
                                },
                                new List<PropertyInfo>
                                {
                                    compatibleInverse
                                })
                        );

                        if (relationshipCandidate.TargetTypeBuilder.Metadata == entityTypeBuilder.Metadata
                            && relationshipCandidate.NavigationProperties.Count == 0
                            && relationshipCandidate.InverseProperties.Count > 0)
                        {
                            var nextSelfRefCandidate = relationshipCandidate.InverseProperties.First();
                            if (!relationshipCandidate.NavigationProperties.Contains(nextSelfRefCandidate))
                            {
                                relationshipCandidate.NavigationProperties.Add(nextSelfRefCandidate);
                            }

                            relationshipCandidate.InverseProperties.Remove(nextSelfRefCandidate);
                        }

                        continue;
                    }

                    NextCandidate:
                    break;
                }

                if (relationshipCandidate.NavigationProperties.Count > 0
                    || relationshipCandidate.InverseProperties.Count > 0)
                {
                    filteredRelationshipCandidates.Add(relationshipCandidate);
                }
                else if (IsCandidateUnusedOwnedType(relationshipCandidate.TargetTypeBuilder.Metadata)
                         && filteredRelationshipCandidates.All(
                             c => c.TargetTypeBuilder.Metadata != relationshipCandidate.TargetTypeBuilder.Metadata))
                {
                    entityTypeBuilder.ModelBuilder
                        .RemoveEntityType(relationshipCandidate.TargetTypeBuilder.Metadata, ConfigurationSource.Convention);
                }
            }

            return filteredRelationshipCandidates;
        }

        private static bool IsCompatibleInverse(
            PropertyInfo navigationProperty,
            PropertyInfo inversePropertyInfo,
            InternalEntityTypeBuilder entityTypeBuilder,
            InternalEntityTypeBuilder targetEntityTypeBuilder)
        {
            var entityType = entityTypeBuilder.Metadata;
            var existingNavigation = entityType.FindNavigation(navigationProperty.GetSimpleMemberName());
            if (existingNavigation != null
                && !CanMergeWith(existingNavigation, inversePropertyInfo, targetEntityTypeBuilder))
            {
                return false;
            }

            var existingInverse = targetEntityTypeBuilder.Metadata.FindNavigation(inversePropertyInfo.Name);
            if (existingInverse != null)
            {
                if (existingInverse.DeclaringEntityType != targetEntityTypeBuilder.Metadata
                    || !CanMergeWith(existingInverse, navigationProperty, entityTypeBuilder))
                {
                    return false;
                }

                var otherEntityType = existingInverse.GetTargetType();
                if (!entityType.ClrType.GetTypeInfo()
                    .IsAssignableFrom(otherEntityType.ClrType.GetTypeInfo()))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanMergeWith(
            Navigation existingNavigation, PropertyInfo inverse, InternalEntityTypeBuilder inverseEntityTypeBuilder)
        {
            var fk = existingNavigation.ForeignKey;
            return (fk.IsSelfReferencing()
                    || fk.ResolveOtherEntityType(existingNavigation.DeclaringEntityType) == inverseEntityTypeBuilder.Metadata)
                   && fk.Builder.CanSetNavigation(inverse, !existingNavigation.IsDependentToPrincipal(), ConfigurationSource.Convention);
        }

        private static IReadOnlyList<RelationshipCandidate> RemoveInheritedInverseNavigations(
            IReadOnlyList<RelationshipCandidate> relationshipCandidates)
        {
            if (relationshipCandidates.Count == 0)
            {
                return relationshipCandidates;
            }

            var relationshipCandidatesByRoot = relationshipCandidates.GroupBy(r => r.TargetTypeBuilder.Metadata.RootType())
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var relationshipCandidatesHierarchy in relationshipCandidatesByRoot.Values)
            {
                var filteredRelationshipCandidates = new HashSet<RelationshipCandidate>();
                foreach (var relationshipCandidate in relationshipCandidatesHierarchy)
                {
                    RemoveInheritedInverseNavigations(
                        relationshipCandidate, relationshipCandidatesHierarchy, filteredRelationshipCandidates);
                }
            }

            return relationshipCandidates;
        }

        private static void RemoveInheritedInverseNavigations(
            RelationshipCandidate relationshipCandidate,
            List<RelationshipCandidate> relationshipCandidatesHierarchy,
            HashSet<RelationshipCandidate> filteredRelationshipCandidates)
        {
            if (filteredRelationshipCandidates.Contains(relationshipCandidate)
                || (relationshipCandidate.NavigationProperties.Count > 1
                    && relationshipCandidate.InverseProperties.Count > 0)
                || relationshipCandidate.InverseProperties.Count > 1)
            {
                return;
            }

            filteredRelationshipCandidates.Add(relationshipCandidate);
            var inverseCandidate = relationshipCandidate.InverseProperties.FirstOrDefault();
            if (inverseCandidate != null)
            {
                var relationshipsToDerivedTypes = relationshipCandidatesHierarchy
                    .Where(r => r.TargetTypeBuilder != relationshipCandidate.TargetTypeBuilder
                                && relationshipCandidate.TargetTypeBuilder.Metadata.IsAssignableFrom(r.TargetTypeBuilder.Metadata));
                foreach (var relationshipToDerivedType in relationshipsToDerivedTypes)
                {
                    relationshipToDerivedType.InverseProperties.RemoveAll(i => i.GetSimpleMemberName() == inverseCandidate.GetSimpleMemberName());

                    if (!filteredRelationshipCandidates.Contains(relationshipToDerivedType))
                    {
                        // An ambiguity might have been resolved
                        RemoveInheritedInverseNavigations(relationshipToDerivedType, relationshipCandidatesHierarchy, filteredRelationshipCandidates);
                    }
                }
            }
        }

        private static IReadOnlyList<RelationshipCandidate> RemoveSingleSidedBaseNavigations(
            IReadOnlyList<RelationshipCandidate> relationshipCandidates,
            InternalEntityTypeBuilder entityTypeBuilder)
        {
            if (relationshipCandidates.Count == 0)
            {
                return relationshipCandidates;
            }

            var filteredRelationshipCandidates = new List<RelationshipCandidate>();
            foreach (var relationshipCandidate in relationshipCandidates)
            {
                if (relationshipCandidate.InverseProperties.Count > 0)
                {
                    filteredRelationshipCandidates.Add(relationshipCandidate);
                    continue;
                }

                foreach (var navigation in relationshipCandidate.NavigationProperties.ToList())
                {
                    if (entityTypeBuilder.Metadata.FindDerivedNavigations(navigation.GetSimpleMemberName()).Any(n => n.FindInverse() != null))
                    {
                        relationshipCandidate.NavigationProperties.Remove(navigation);
                    }
                }

                if (relationshipCandidate.NavigationProperties.Count > 0)
                {
                    filteredRelationshipCandidates.Add(relationshipCandidate);
                }
                else if (IsCandidateUnusedOwnedType(relationshipCandidate.TargetTypeBuilder.Metadata)
                         && filteredRelationshipCandidates.All(
                             c => c.TargetTypeBuilder.Metadata != relationshipCandidate.TargetTypeBuilder.Metadata))
                {
                    entityTypeBuilder.ModelBuilder
                        .RemoveEntityType(relationshipCandidate.TargetTypeBuilder.Metadata, ConfigurationSource.Convention);
                }
            }

            return filteredRelationshipCandidates;
        }

        private void CreateRelationships(
            IEnumerable<RelationshipCandidate> relationshipCandidates, InternalEntityTypeBuilder entityTypeBuilder)
        {
            var unusedEntityTypes = new List<EntityType>();
            foreach (var relationshipCandidate in relationshipCandidates)
            {
                var entityType = entityTypeBuilder.Metadata;
                var targetEntityType = relationshipCandidate.TargetTypeBuilder.Metadata;
                var isAmbiguousOnBase = entityType.BaseType != null
                                        && HasAmbiguousNavigationsTo(
                                            entityType.BaseType, targetEntityType.ClrType)
                                        || targetEntityType.BaseType != null
                                        && HasAmbiguousNavigationsTo(
                                            targetEntityType.BaseType, entityType.ClrType);

                var ambiguousOwnership = relationshipCandidate.NavigationProperties.Count == 1
                                         && relationshipCandidate.InverseProperties.Count == 1
                                         && entityType.GetConfigurationSource() != ConfigurationSource.Explicit
                                         && targetEntityType.GetConfigurationSource() != ConfigurationSource.Explicit
                                         && targetEntityType.Model.ShouldBeOwnedType(entityType.ClrType)
                                         && targetEntityType.Model.ShouldBeOwnedType(targetEntityType.ClrType);

                if (ambiguousOwnership)
                {
                    var existingNavigation = entityType.FindNavigation(relationshipCandidate.NavigationProperties.Single().GetSimpleMemberName());
                    if (existingNavigation != null
                         && existingNavigation.ForeignKey.DeclaringEntityType == targetEntityType
                         && existingNavigation.ForeignKey.GetPrincipalEndConfigurationSource().OverridesStrictly(ConfigurationSource.Convention))
                    {
                        ambiguousOwnership = false;
                    }
                    else
                    {
                        var existingInverse = targetEntityType.FindNavigation(relationshipCandidate.InverseProperties.Single().GetSimpleMemberName());
                        if (existingInverse != null
                            && existingInverse.ForeignKey.PrincipalEntityType == targetEntityType
                            && existingInverse.ForeignKey.GetPrincipalEndConfigurationSource().OverridesStrictly(ConfigurationSource.Convention))
                        {
                            ambiguousOwnership = false;
                        }
                    }
                }

                if ((relationshipCandidate.NavigationProperties.Count > 1
                     && relationshipCandidate.InverseProperties.Count > 0
                     && (!targetEntityType.Model.ShouldBeOwnedType(targetEntityType.ClrType)
                         || entityType.IsInOwnershipPath(targetEntityType)))
                    || relationshipCandidate.InverseProperties.Count > 1
                    || isAmbiguousOnBase
                    || ambiguousOwnership
                    || HasDeclaredAmbiguousNavigationsTo(entityType, targetEntityType.ClrType)
                    || HasDeclaredAmbiguousNavigationsTo(targetEntityType, entityType.ClrType))
                {
                    if (!isAmbiguousOnBase)
                    {
                        _logger.MultipleNavigationProperties(
                            relationshipCandidate.NavigationProperties.Count == 0
                                ? new[] { new Tuple<MemberInfo, Type>(null, targetEntityType.ClrType) }
                                : relationshipCandidate.NavigationProperties.Select(n => new Tuple<MemberInfo, Type>(n, entityType.ClrType)),
                            relationshipCandidate.InverseProperties.Count == 0
                                ? new[] { new Tuple<MemberInfo, Type>(null, targetEntityType.ClrType) }
                                : relationshipCandidate.InverseProperties.Select(n => new Tuple<MemberInfo, Type>(n, targetEntityType.ClrType)));
                    }

                    foreach (var navigationProperty in relationshipCandidate.NavigationProperties.ToList())
                    {
                        var existingNavigation = entityType.FindDeclaredNavigation(navigationProperty.GetSimpleMemberName());
                        if (existingNavigation != null
                            && existingNavigation.ForeignKey.DeclaringEntityType.Builder
                                .RemoveForeignKey(existingNavigation.ForeignKey, ConfigurationSource.Convention) == null
                            && existingNavigation.ForeignKey.Builder.Navigations(
                                existingNavigation.IsDependentToPrincipal() ? PropertyIdentity.None : (PropertyIdentity?)null,
                                existingNavigation.IsDependentToPrincipal() ? (PropertyIdentity?)null : PropertyIdentity.None,
                                ConfigurationSource.Convention) == null)
                        {
                            // Navigations of higher configuration source are not ambiguous
                            relationshipCandidate.NavigationProperties.Remove(navigationProperty);
                        }
                    }

                    foreach (var inverseProperty in relationshipCandidate.InverseProperties.ToList())
                    {
                        var existingInverse = targetEntityType.FindDeclaredNavigation(inverseProperty.GetSimpleMemberName());
                        if (existingInverse != null
                            && existingInverse.ForeignKey.DeclaringEntityType.Builder
                                .RemoveForeignKey(existingInverse.ForeignKey, ConfigurationSource.Convention) == null
                            && existingInverse.ForeignKey.Builder.Navigations(
                                existingInverse.IsDependentToPrincipal() ? PropertyIdentity.None : (PropertyIdentity?)null,
                                existingInverse.IsDependentToPrincipal() ? (PropertyIdentity?)null : PropertyIdentity.None,
                                ConfigurationSource.Convention) == null)
                        {
                            // Navigations of higher configuration source are not ambiguous
                            relationshipCandidate.InverseProperties.Remove(inverseProperty);
                        }
                    }

                    if (!isAmbiguousOnBase)
                    {
                        AddAmbiguous(entityTypeBuilder, relationshipCandidate.NavigationProperties, targetEntityType.ClrType);

                        AddAmbiguous(targetEntityType.Builder, relationshipCandidate.InverseProperties, entityType.ClrType);
                    }

                    unusedEntityTypes.Add(targetEntityType);

                    continue;
                }

                foreach (var navigation in relationshipCandidate.NavigationProperties)
                {
                    if (targetEntityType.Builder == null
                        && !targetEntityType.Model.ShouldBeOwnedType(targetEntityType.ClrType))
                    {
                        continue;
                    }

                    if (InversePropertyAttributeConvention.IsAmbiguous(entityType, navigation, targetEntityType))
                    {
                        unusedEntityTypes.Add(targetEntityType);
                        continue;
                    }

                    var targetOwned = targetEntityType.Model.ShouldBeOwnedType(targetEntityType.ClrType)
                                      && !entityType.IsInOwnershipPath(targetEntityType);

                    var inverse = relationshipCandidate.InverseProperties.SingleOrDefault();
                    if (inverse == null)
                    {
                        if (targetOwned)
                        {
                            entityTypeBuilder.Owns(
                                targetEntityType.ClrType,
                                navigation,
                                ConfigurationSource.Convention);
                        }
                        else
                        {
                            entityTypeBuilder.Navigation(
                                targetEntityType.Builder,
                                navigation,
                                ConfigurationSource.Convention);
                        }
                    }
                    else
                    {
                        if (InversePropertyAttributeConvention.IsAmbiguous(targetEntityType, inverse, entityType))
                        {
                            unusedEntityTypes.Add(targetEntityType);
                            continue;
                        }

                        if (targetOwned
                            && entityType.Model.ShouldBeOwnedType(entityType.ClrType))
                        {
                            var existingInverse = targetEntityType.FindNavigation(inverse.GetSimpleMemberName());
                            if (inverse.PropertyType.TryGetSequenceType() != null
                                || targetEntityType.GetConfigurationSource() == ConfigurationSource.Explicit
                                || (existingInverse != null
                                    && existingInverse.ForeignKey.DeclaringEntityType == entityType
                                    && existingInverse.ForeignKey.GetPrincipalEndConfigurationSource().OverridesStrictly(ConfigurationSource.Convention)))
                            {
                                // Target type is the principal, so the ownership should be configured from the other side
                                targetOwned = false;
                            }
                        }

                        if (targetOwned)
                        {
                            entityTypeBuilder.Owns(
                                targetEntityType.ClrType,
                                navigation,
                                inverse,
                                ConfigurationSource.Convention);
                        }
                        else
                        {
                            entityTypeBuilder.Relationship(
                                targetEntityType.Builder,
                                navigation,
                                inverse,
                                ConfigurationSource.Convention);
                        }
                    }
                }

                if (relationshipCandidate.NavigationProperties.Count == 0)
                {
                    if (relationshipCandidate.InverseProperties.Count == 0
                        || targetEntityType.Model.ShouldBeOwnedType(targetEntityType.ClrType))
                    {
                        unusedEntityTypes.Add(targetEntityType);
                    }
                    else
                    {
                        foreach (var inverse in relationshipCandidate.InverseProperties)
                        {
                            if (targetEntityType.Builder == null)
                            {
                                continue;
                            }

                            if (InversePropertyAttributeConvention.IsAmbiguous(targetEntityType, inverse, entityType))
                            {
                                unusedEntityTypes.Add(targetEntityType);
                                continue;
                            }

                            targetEntityType.Builder.Navigation(
                                entityTypeBuilder,
                                inverse,
                                ConfigurationSource.Convention);
                        }
                    }
                }
            }

            foreach (var unusedEntityType in unusedEntityTypes)
            {
                if (IsCandidateUnusedOwnedType(unusedEntityType)
                    && unusedEntityType.DefiningEntityType.FindNavigation(unusedEntityType.DefiningNavigationName) == null)
                {
                    entityTypeBuilder.ModelBuilder.RemoveEntityType(unusedEntityType, ConfigurationSource.Convention);
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalEntityTypeBuilder Apply(InternalEntityTypeBuilder entityTypeBuilder)
            => !entityTypeBuilder.Metadata.HasClrType() ? entityTypeBuilder : DiscoverRelationships(entityTypeBuilder);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool Apply(InternalEntityTypeBuilder entityTypeBuilder, EntityType oldBaseType)
        {
            var oldBaseTypeBuilder = oldBaseType?.Builder;
            if (oldBaseTypeBuilder != null)
            {
                DiscoverRelationships(oldBaseTypeBuilder);
            }

            ApplyOnRelatedEntityTypes(entityTypeBuilder.Metadata);
            foreach (var entityType in entityTypeBuilder.Metadata.GetDerivedTypesInclusive())
            {
                DiscoverRelationships(entityType.Builder);
            }

            return true;
        }

        private void ApplyOnRelatedEntityTypes(EntityType entityType)
        {
            var relatedEntityTypes = entityType.GetReferencingForeignKeys().Select(fk => fk.DeclaringEntityType)
                .Concat(entityType.GetForeignKeys().Select(fk => fk.PrincipalEntityType))
                .Distinct()
                .ToList();

            foreach (var relatedEntityType in relatedEntityTypes)
            {
                var relatedEntityTypeBuilder = relatedEntityType.Builder;
                DiscoverRelationships(relatedEntityTypeBuilder);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool Apply(
            InternalEntityTypeBuilder sourceEntityTypeBuilder,
            InternalEntityTypeBuilder targetEntityTypeBuilder,
            string navigationName,
            MemberInfo propertyInfo)
            => (targetEntityTypeBuilder.Metadata.Builder == null
                && sourceEntityTypeBuilder.ModelBuilder.IsIgnored(
                    targetEntityTypeBuilder.Metadata.Name, ConfigurationSource.Convention))
               || !IsCandidateNavigationProperty(sourceEntityTypeBuilder, navigationName, propertyInfo)
               || Apply(sourceEntityTypeBuilder.Metadata, propertyInfo);

        private bool Apply(EntityType entityType, MemberInfo navigationProperty)
        {
            DiscoverRelationships(entityType.Builder);
            if (entityType.FindNavigation(navigationProperty.GetSimpleMemberName()) != null)
            {
                return false;
            }

            if (IsAmbiguous(entityType, navigationProperty))
            {
                return true;
            }

            foreach (var derivedEntityType in entityType.GetDirectlyDerivedTypes())
            {
                Apply(derivedEntityType, navigationProperty);
            }

            return true;
        }

        [ContractAnnotation("memberInfo:null => false")]
        private static bool IsCandidateNavigationProperty(
            InternalEntityTypeBuilder sourceEntityTypeBuilder, string navigationName, MemberInfo memberInfo)
            => memberInfo != null
               && sourceEntityTypeBuilder?.IsIgnored(navigationName, ConfigurationSource.Convention) == false
               && sourceEntityTypeBuilder.Metadata.FindProperty(navigationName) == null
               && sourceEntityTypeBuilder.Metadata.FindServiceProperty(navigationName) == null
               && (!(memberInfo is PropertyInfo propertyInfo) || propertyInfo.GetIndexParameters().Length == 0)
               && (!sourceEntityTypeBuilder.Metadata.IsQueryType
                   || (memberInfo as PropertyInfo)?.PropertyType.TryGetSequenceType() == null);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool Apply(InternalEntityTypeBuilder entityTypeBuilder, string ignoredMemberName)
        {
            var anyAmbiguityRemoved = false;
            foreach (var derivedEntityType in entityTypeBuilder.Metadata.GetDerivedTypesInclusive())
            {
                var ambigousNavigations = GetAmbigousNavigations(derivedEntityType);
                if (ambigousNavigations == null)
                {
                    continue;
                }

                KeyValuePair<MemberInfo, Type>? ambigousNavigation = null;
                foreach (var navigation in ambigousNavigations)
                {
                    if (navigation.Key.GetSimpleMemberName() == ignoredMemberName)
                    {
                        ambigousNavigation = navigation;
                    }
                }

                if (ambigousNavigation == null)
                {
                    continue;
                }

                anyAmbiguityRemoved = true;

                var targetClrType = ambigousNavigation.Value.Value;
                RemoveAmbiguous(derivedEntityType, targetClrType);

                var targetType = GetTargetEntityTypeBuilder(
                    entityTypeBuilder, targetClrType, ambigousNavigation.Value.Key, null)?.Metadata;
                if (targetType != null)
                {
                    RemoveAmbiguous(targetType, derivedEntityType.ClrType);
                }
            }

            if (anyAmbiguityRemoved)
            {
                DiscoverRelationships(entityTypeBuilder);
            }

            return true;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Apply(InternalRelationshipBuilder relationshipBuilder, Navigation navigation)
        {
            foreach (var entityType in navigation.DeclaringEntityType.GetDerivedTypesInclusive())
            {
                // Only run the convention if an ambiguity might have been removed
                var ambiguityRemoved = RemoveAmbiguous(entityType, navigation.GetTargetType().ClrType);
                var targetAmbiguityRemoved = RemoveAmbiguous(navigation.GetTargetType(), entityType.ClrType);

                if (ambiguityRemoved)
                {
                    DiscoverRelationships(entityType.Builder);
                }

                if (targetAmbiguityRemoved)
                {
                    DiscoverRelationships(navigation.GetTargetType().Builder);
                }
            }

            if (relationshipBuilder.Metadata.Builder == null)
            {
                relationshipBuilder = navigation.DeclaringEntityType.FindNavigation(navigation.Name)?.ForeignKey?.Builder;
            }

            return relationshipBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        InternalRelationshipBuilder IForeignKeyOwnershipChangedConvention.Apply(InternalRelationshipBuilder relationshipBuilder)
        {
            DiscoverRelationships(relationshipBuilder.Metadata.DeclaringEntityType.Builder);
            return relationshipBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Type FindCandidateNavigationPropertyType([NotNull] PropertyInfo propertyInfo)
            => _memberClassifier.FindCandidateNavigationPropertyType(propertyInfo);

        private ImmutableSortedDictionary<PropertyInfo, Type> GetNavigationCandidates(EntityType entityType)
        {
            if (entityType.FindAnnotation(NavigationCandidatesAnnotationName)?.Value
                is ImmutableSortedDictionary<PropertyInfo, Type> navigationCandidates)
            {
                return navigationCandidates;
            }

            var dictionaryBuilder = ImmutableSortedDictionary.CreateBuilder<PropertyInfo, Type>(MemberInfoNameComparer.Instance);
            if (entityType.HasClrType())
            {
                foreach (var propertyInfo in entityType.GetRuntimeProperties().Values.OrderBy(p => p.Name))
                {
                    var targetType = FindCandidateNavigationPropertyType(propertyInfo);
                    if (targetType != null)
                    {
                        dictionaryBuilder[propertyInfo] = targetType;
                    }
                }
            }

            navigationCandidates = dictionaryBuilder.ToImmutable();
            SetNavigationCandidates(entityType.Builder, navigationCandidates);
            return navigationCandidates;
        }

        private static void SetNavigationCandidates(
            InternalEntityTypeBuilder entityTypeBuilder,
            ImmutableSortedDictionary<PropertyInfo, Type> navigationCandidates)
            => entityTypeBuilder.HasAnnotation(NavigationCandidatesAnnotationName, navigationCandidates, ConfigurationSource.Convention);

        private static bool IsCandidateUnusedOwnedType(EntityType entityType)
            => entityType.HasDefiningNavigation() && !entityType.GetForeignKeys().Any();

        private static bool IsAmbiguous(EntityType entityType, MemberInfo navigationProperty)
        {
            while (entityType != null)
            {
                var ambigousNavigations = GetAmbigousNavigations(entityType);
                if (ambigousNavigations?.ContainsKey(navigationProperty) == true)
                {
                    return true;
                }

                entityType = entityType.BaseType;
            }

            return false;
        }

        private static bool HasAmbiguousNavigationsTo(EntityType sourceEntityType, Type targetClrType)
        {
            while (sourceEntityType != null)
            {
                if (HasDeclaredAmbiguousNavigationsTo(sourceEntityType, targetClrType))
                {
                    return true;
                }

                sourceEntityType = sourceEntityType.BaseType;
            }

            return false;
        }

        private static bool HasDeclaredAmbiguousNavigationsTo(EntityType sourceEntityType, Type targetClrType)
        {
            var ambigousNavigations = GetAmbigousNavigations(sourceEntityType);
            return ambigousNavigations?.ContainsValue(targetClrType) == true;
        }

        private static ImmutableSortedDictionary<MemberInfo, Type> GetAmbigousNavigations(EntityType entityType)
            => entityType.FindAnnotation(AmbiguousNavigationsAnnotationName)?.Value
                as ImmutableSortedDictionary<MemberInfo, Type>;

        private static void AddAmbiguous(
            InternalEntityTypeBuilder entityTypeBuilder, IReadOnlyList<PropertyInfo> navigationProperties, Type targetType)
        {
            if (navigationProperties.Count == 0)
            {
                return;
            }

            var currentAmbiguousNavigations = GetAmbigousNavigations(entityTypeBuilder.Metadata);
            var newAmbiguousNavigations = ImmutableSortedDictionary.CreateRange(
                MemberInfoNameComparer.Instance,
                navigationProperties.Where(n => currentAmbiguousNavigations?.ContainsKey(n) != true)
                    .Select(n => new KeyValuePair<MemberInfo, Type>(n, targetType)));

            if (currentAmbiguousNavigations != null)
            {
                newAmbiguousNavigations = newAmbiguousNavigations.Count > 0
                    ? currentAmbiguousNavigations.AddRange(newAmbiguousNavigations)
                    : currentAmbiguousNavigations;
            }

            SetAmbigousNavigations(entityTypeBuilder, newAmbiguousNavigations);
        }

        private static bool RemoveAmbiguous(EntityType entityType, Type targetType)
        {
            var ambigousNavigations = GetAmbigousNavigations(entityType);
            if (ambigousNavigations?.IsEmpty == false)
            {
                var newAmbigousNavigations = ambigousNavigations;
                foreach (var ambigousNavigation in ambigousNavigations)
                {
                    if (targetType.IsAssignableFrom(ambigousNavigation.Value))
                    {
                        newAmbigousNavigations = newAmbigousNavigations.Remove(ambigousNavigation.Key);
                    }
                }

                if (ambigousNavigations.Count != newAmbigousNavigations.Count)
                {
                    SetAmbigousNavigations(entityType.Builder, newAmbigousNavigations);
                    return true;
                }
            }

            return false;
        }

        private static void SetAmbigousNavigations(
            InternalEntityTypeBuilder entityTypeBuilder,
            ImmutableSortedDictionary<MemberInfo, Type> ambiguousNavigations)
            => entityTypeBuilder.HasAnnotation(AmbiguousNavigationsAnnotationName, ambiguousNavigations, ConfigurationSource.Convention);

        private class MemberInfoNameComparer : IComparer<MemberInfo>
        {
            public static readonly MemberInfoNameComparer Instance = new MemberInfoNameComparer();

            private MemberInfoNameComparer()
            {
            }

            public int Compare(MemberInfo x, MemberInfo y) => StringComparer.Ordinal.Compare(x.Name, y.Name);
        }

        private class RelationshipCandidate
        {
            public RelationshipCandidate(
                InternalEntityTypeBuilder targetTypeBuilder,
                List<PropertyInfo> navigations,
                List<PropertyInfo> inverseNavigations)
            {
                TargetTypeBuilder = targetTypeBuilder;
                NavigationProperties = navigations;
                InverseProperties = inverseNavigations;
            }

            public InternalEntityTypeBuilder TargetTypeBuilder { [DebuggerStepThrough] get; }
            public List<PropertyInfo> NavigationProperties { [DebuggerStepThrough] get; }
            public List<PropertyInfo> InverseProperties { [DebuggerStepThrough] get; }
        }
    }
}
