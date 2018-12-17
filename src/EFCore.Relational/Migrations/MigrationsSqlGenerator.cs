// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Migrations
{
    /// <summary>
    ///     <para>
    ///         Generates the SQL in <see cref="MigrationCommand" /> objects that can
    ///         then be executed or scripted from a list of <see cref="MigrationOperation" />s.
    ///     </para>
    ///     <para>
    ///         This class is typically inherited by database providers to customize the SQL generation.
    ///     </para>
    /// </summary>
    public class MigrationsSqlGenerator : IMigrationsSqlGenerator
    {
        private static readonly IReadOnlyDictionary<Type, Action<MigrationsSqlGenerator, MigrationOperation, IModel, MigrationCommandListBuilder>> _generateActions =
            new Dictionary<Type, Action<MigrationsSqlGenerator, MigrationOperation, IModel, MigrationCommandListBuilder>>
            {
                { typeof(AddColumnOperation), (g, o, m, b) => g.Generate((AddColumnOperation)o, m, b) },
                { typeof(AddForeignKeyOperation), (g, o, m, b) => g.Generate((AddForeignKeyOperation)o, m, b) },
                { typeof(AddPrimaryKeyOperation), (g, o, m, b) => g.Generate((AddPrimaryKeyOperation)o, m, b) },
                { typeof(AddUniqueConstraintOperation), (g, o, m, b) => g.Generate((AddUniqueConstraintOperation)o, m, b) },
                { typeof(AlterColumnOperation), (g, o, m, b) => g.Generate((AlterColumnOperation)o, m, b) },
                { typeof(AlterDatabaseOperation), (g, o, m, b) => g.Generate((AlterDatabaseOperation)o, m, b) },
                { typeof(AlterSequenceOperation), (g, o, m, b) => g.Generate((AlterSequenceOperation)o, m, b) },
                { typeof(AlterTableOperation), (g, o, m, b) => g.Generate((AlterTableOperation)o, m, b) },
                { typeof(CreateIndexOperation), (g, o, m, b) => g.Generate((CreateIndexOperation)o, m, b) },
                { typeof(CreateSequenceOperation), (g, o, m, b) => g.Generate((CreateSequenceOperation)o, m, b) },
                { typeof(CreateTableOperation), (g, o, m, b) => g.Generate((CreateTableOperation)o, m, b) },
                { typeof(DropColumnOperation), (g, o, m, b) => g.Generate((DropColumnOperation)o, m, b) },
                { typeof(DropForeignKeyOperation), (g, o, m, b) => g.Generate((DropForeignKeyOperation)o, m, b) },
                { typeof(DropIndexOperation), (g, o, m, b) => g.Generate((DropIndexOperation)o, m, b) },
                { typeof(DropPrimaryKeyOperation), (g, o, m, b) => g.Generate((DropPrimaryKeyOperation)o, m, b) },
                { typeof(DropSchemaOperation), (g, o, m, b) => g.Generate((DropSchemaOperation)o, m, b) },
                { typeof(DropSequenceOperation), (g, o, m, b) => g.Generate((DropSequenceOperation)o, m, b) },
                { typeof(DropTableOperation), (g, o, m, b) => g.Generate((DropTableOperation)o, m, b) },
                { typeof(DropUniqueConstraintOperation), (g, o, m, b) => g.Generate((DropUniqueConstraintOperation)o, m, b) },
                { typeof(EnsureSchemaOperation), (g, o, m, b) => g.Generate((EnsureSchemaOperation)o, m, b) },
                { typeof(RenameColumnOperation), (g, o, m, b) => g.Generate((RenameColumnOperation)o, m, b) },
                { typeof(RenameIndexOperation), (g, o, m, b) => g.Generate((RenameIndexOperation)o, m, b) },
                { typeof(RenameSequenceOperation), (g, o, m, b) => g.Generate((RenameSequenceOperation)o, m, b) },
                { typeof(RenameTableOperation), (g, o, m, b) => g.Generate((RenameTableOperation)o, m, b) },
                { typeof(RestartSequenceOperation), (g, o, m, b) => g.Generate((RestartSequenceOperation)o, m, b) },
                { typeof(SqlOperation), (g, o, m, b) => g.Generate((SqlOperation)o, m, b) },
                { typeof(InsertDataOperation), (g, o, m, b) => g.Generate((InsertDataOperation)o, m, b) },
                { typeof(DeleteDataOperation), (g, o, m, b) => g.Generate((DeleteDataOperation)o, m, b) },
                { typeof(UpdateDataOperation), (g, o, m, b) => g.Generate((UpdateDataOperation)o, m, b) }
            };

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        public MigrationsSqlGenerator([NotNull] MigrationsSqlGeneratorDependencies dependencies)
        {
            Check.NotNull(dependencies, nameof(dependencies));

            Dependencies = dependencies;
        }

        /// <summary>
        ///     Parameter object containing dependencies for this service.
        /// </summary>
        protected virtual MigrationsSqlGeneratorDependencies Dependencies { get; }

        /// <summary>
        ///     The <see cref="IUpdateSqlGenerator" />.
        /// </summary>
        protected virtual IUpdateSqlGenerator SqlGenerator
            => (IUpdateSqlGenerator)Dependencies.UpdateSqlGenerator;

        /// <summary>
        ///     Gets a comparer that can be used to compare two product versions.
        /// </summary>
        protected virtual IComparer<string> VersionComparer { get; } = new SemanticVersionComparer();

        /// <summary>
        ///     Generates commands from a list of operations.
        /// </summary>
        /// <param name="operations"> The operations. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <returns> The list of commands to be executed or scripted. </returns>
        public virtual IReadOnlyList<MigrationCommand> Generate(
            IReadOnlyList<MigrationOperation> operations,
            IModel model = null)
        {
            Check.NotNull(operations, nameof(operations));

            var builder = new MigrationCommandListBuilder(Dependencies.CommandBuilderFactory);
            foreach (var operation in operations)
            {
                Generate(operation, model, builder);
            }

            return builder.GetCommandList();
        }

        /// <summary>
        ///     <para>
        ///         Builds commands for the given <see cref="MigrationOperation" /> by making calls on the given
        ///         <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         This method uses a double-dispatch mechanism to call one of the 'Generate' methods that are
        ///         specific to a certain subtype of <see cref="MigrationOperation" />. Typically database providers
        ///         will override these specific methods rather than this method. However, providers can override
        ///         this methods to handle provider-specific operations.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] MigrationOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var operationType = operation.GetType();
            if (!_generateActions.TryGetValue(operationType, out var generateAction))
            {
                throw new InvalidOperationException(RelationalStrings.UnknownOperation(GetType().ShortDisplayName(), operationType));
            }

            generateAction(this, operation, model, builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AddColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="AddColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] AddColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ADD ");

            ColumnDefinition(operation, model, builder);

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AddForeignKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="AddForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] AddForeignKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ADD ");

            ForeignKeyConstraint(operation, model, builder);

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AddPrimaryKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="AddPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] AddPrimaryKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ADD ");
            PrimaryKeyConstraint(operation, model, builder);

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddUniqueConstraintOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AddUniqueConstraintOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ADD ");
            UniqueConstraint(operation, model, builder);
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="AlterColumnOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AlterColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="AlterDatabaseOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that there is no default implementation of this method. Providers must override if they are to
        ///         support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AlterDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="RenameIndexOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] RenameIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AlterSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

            SequenceOptions(operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="AlterTableOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method does nothing because there is no common metadata
        ///         relating to this operation. Providers only need to override this method if they have some provider-specific
        ///         annotations that must be handled.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] AlterTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="RenameTableOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] RenameTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateIndexOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] CreateIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="CreateIndexOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] CreateIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder.Append("CREATE ");

            if (operation.IsUnique)
            {
                builder.Append("UNIQUE ");
            }

            IndexTraits(operation, model, builder);

            builder
                .Append("INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" (")
                .Append(ColumnList(operation.Columns))
                .Append(")");

            IndexOptions(operation, model, builder);

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="EnsureSchemaOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] EnsureSchemaOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] CreateSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("CREATE SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

            var typeMapping = Dependencies.TypeMappingSource.GetMapping(operation.ClrType);

            if (operation.ClrType != typeof(long))
            {
                builder
                    .Append(" AS ")
                    .Append(typeMapping.StoreType);

                // set the typeMapping for use with operation.StartValue (i.e. a long) below
                typeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));
            }

            builder
                .Append(" START WITH ")
                .Append(typeMapping.GenerateSqlLiteral(operation.StartValue));

            SequenceOptions(operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] CreateTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="CreateTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] CreateTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("CREATE TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .AppendLine(" (");

            using (builder.Indent())
            {
                for (var i = 0; i < operation.Columns.Count; i++)
                {
                    var column = operation.Columns[i];
                    ColumnDefinition(column, model, builder);

                    if (i != operation.Columns.Count - 1)
                    {
                        builder.AppendLine(",");
                    }
                }

                if (operation.PrimaryKey != null)
                {
                    builder.AppendLine(",");
                    PrimaryKeyConstraint(operation.PrimaryKey, model, builder);
                }

                foreach (var uniqueConstraint in operation.UniqueConstraints)
                {
                    builder.AppendLine(",");
                    UniqueConstraint(uniqueConstraint, model, builder);
                }

                foreach (var foreignKey in operation.ForeignKeys)
                {
                    builder.AppendLine(",");
                    ForeignKeyConstraint(foreignKey, model, builder);
                }

                builder.AppendLine();
            }

            builder.Append(")");

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] DropColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropForeignKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] DropForeignKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="DropIndexOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropPrimaryKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] DropPrimaryKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropSchemaOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropSchemaOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP SCHEMA ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] DropTableOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropUniqueConstraintOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DropUniqueConstraintOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="RenameColumnOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] RenameColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     <para>
        ///         Can be overridden by database providers to build commands for the given <see cref="RenameSequenceOperation" />
        ///         by making calls on the given <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         Note that the default implementation of this method throws <see cref="NotImplementedException" />. Providers
        ///         must override if they are to support this kind of operation.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] RenameSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RestartSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] RestartSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

            builder
                .Append("ALTER SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" RESTART WITH ")
                .Append(longTypeMapping.GenerateSqlLiteral(operation.StartValue))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="SqlOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] SqlOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder.AppendLine(operation.Sql);

            EndStatement(builder, operation.SuppressTransaction);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="InsertDataOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] InsertDataOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="InsertDataOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] InsertDataOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var sqlBuilder = new StringBuilder();
            foreach (var modificationCommand in operation.GenerateModificationCommands(model))
            {
                SqlGenerator.AppendInsertOperation(
                    sqlBuilder,
                    modificationCommand,
                    0);
            }

            builder.Append(sqlBuilder.ToString());

            if (terminate)
            {
                EndStatement(builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DeleteDataOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] DeleteDataOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var sqlBuilder = new StringBuilder();
            foreach (var modificationCommand in operation.GenerateModificationCommands(model))
            {
                SqlGenerator.AppendDeleteOperation(
                    sqlBuilder,
                    modificationCommand,
                    0);
            }

            builder.Append(sqlBuilder.ToString());
            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="UpdateDataOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] UpdateDataOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var sqlBuilder = new StringBuilder();
            foreach (var modificationCommand in operation.GenerateModificationCommands(model))
            {
                SqlGenerator.AppendUpdateOperation(
                    sqlBuilder,
                    modificationCommand,
                    0);
            }

            builder.Append(sqlBuilder.ToString());
            EndStatement(builder);
        }

        /// <summary>
        ///     Generates a SQL fragment configuring a sequence in a <see cref="AlterSequenceOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void SequenceOptions(
            [NotNull] AlterSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => SequenceOptions(
                operation.Schema,
                operation.Name,
                operation.IncrementBy,
                operation.MinValue,
                operation.MaxValue,
                operation.IsCyclic,
                model,
                builder);

        /// <summary>
        ///     Generates a SQL fragment configuring a sequence in a <see cref="CreateSequenceOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void SequenceOptions(
            [NotNull] CreateSequenceOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => SequenceOptions(
                operation.Schema,
                operation.Name,
                operation.IncrementBy,
                operation.MinValue,
                operation.MaxValue,
                operation.IsCyclic,
                model,
                builder);

        /// <summary>
        ///     Generates a SQL fragment configuring a sequence with the given options.
        /// </summary>
        /// <param name="schema"> The schema that contains the sequence, or <c>null</c> to use the default schema. </param>
        /// <param name="name"> The sequence name. </param>
        /// <param name="increment"> The amount to increment by to generate the next value in the sequence. </param>
        /// <param name="minimumValue"> The minimum value supported by the sequence, or <c>null</c> if none was specified. </param>
        /// <param name="maximumValue"> The maximum value supported by the sequence, or <c>null</c> if none was specified. </param>
        /// <param name="cycle"> Indicates whether or not the sequence will start again once the maximum value is reached. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void SequenceOptions(
            [CanBeNull] string schema,
            [NotNull] string name,
            int increment,
            long? minimumValue,
            long? maximumValue,
            bool cycle,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(increment, nameof(increment));
            Check.NotNull(cycle, nameof(cycle));
            Check.NotNull(builder, nameof(builder));

            var intTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(int));
            var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

            builder
                .Append(" INCREMENT BY ")
                .Append(intTypeMapping.GenerateSqlLiteral(increment));

            if (minimumValue != null)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(longTypeMapping.GenerateSqlLiteral(minimumValue));
            }
            else
            {
                builder.Append(" NO MINVALUE");
            }

            if (maximumValue != null)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(longTypeMapping.GenerateSqlLiteral(maximumValue));
            }
            else
            {
                builder.Append(" NO MAXVALUE");
            }

            builder.Append(cycle ? " CYCLE" : " NO CYCLE");
        }

        /// <summary>
        ///     <para>
        ///         Generates a SQL fragment for a column definition in an <see cref="AddColumnOperation" />.
        ///     </para>
        ///     <para>
        ///         This method does not call the overload of ColumnDefinition that takes fixedLength because doing so
        ///         would be a breaking change for providers that have overridden the method without fixedLength.
        ///         Therefore, providers must explicitly override this method to get fixedLength functionality.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void ColumnDefinition(
            [NotNull] AddColumnOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                // operation.FixedLength not included--see comment above.
                operation.IsRowVersion,
                operation.IsNullable,
                operation.DefaultValue,
                operation.DefaultValueSql,
                operation.ComputedColumnSql,
                operation,
                model,
                builder);

        /// <summary>
        ///     Generates a SQL fragment for a column definition for the given column metadata.
        /// </summary>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="table"> The table that contains the column. </param>
        /// <param name="name"> The column name. </param>
        /// <param name="clrType"> The CLR <see cref="Type" /> that the column is mapped to. </param>
        /// <param name="type"> The database/store type for the column, or <c>null</c> if none has been specified. </param>
        /// <param name="unicode">
        ///     Indicates whether or not the column can contain Unicode data, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="maxLength">
        ///     The maximum amount of data that the column can contain, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="rowVersion">
        ///     Indicates whether or not this column is an automatic concurrency token, such as a SQL Server timestamp/rowversion.
        /// </param>
        /// <param name="nullable"> Indicates whether or not the column can store <c>NULL</c> values. </param>
        /// <param name="defaultValue"> The default value for the column. </param>
        /// <param name="defaultValueSql"> The SQL expression to use for the column's default constraint. </param>
        /// <param name="computedColumnSql"> The SQL expression to use to compute the column value. </param>
        /// <param name="annotatable"> The <see cref="MigrationOperation" /> to use to find any custom annotations. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void ColumnDefinition(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] Type clrType,
            [CanBeNull] string type,
            bool? unicode,
            int? maxLength,
            bool rowVersion,
            bool nullable,
            [CanBeNull] object defaultValue,
            [CanBeNull] string defaultValueSql,
            [CanBeNull] string computedColumnSql,
            [NotNull] IAnnotatable annotatable,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => ColumnDefinition(
                schema, table, name, clrType, type, unicode, maxLength, null,
                rowVersion, nullable, defaultValue, defaultValueSql, computedColumnSql, annotatable, model, builder);

        /// <summary>
        ///     Generates a SQL fragment for a column definition for the given column metadata.
        /// </summary>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="table"> The table that contains the column. </param>
        /// <param name="name"> The column name. </param>
        /// <param name="clrType"> The CLR <see cref="Type" /> that the column is mapped to. </param>
        /// <param name="type"> The database/store type for the column, or <c>null</c> if none has been specified. </param>
        /// <param name="unicode">
        ///     Indicates whether or not the column can contain Unicode data, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="maxLength">
        ///     The maximum amount of data that the column can contain, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="fixedLength"> Indicates whether or not the column is constrained to fixed-length data. </param>
        /// <param name="rowVersion">
        ///     Indicates whether or not this column is an automatic concurrency token, such as a SQL Server timestamp/rowversion.
        /// </param>
        /// <param name="nullable"> Indicates whether or not the column can store <c>NULL</c> values. </param>
        /// <param name="defaultValue"> The default value for the column. </param>
        /// <param name="defaultValueSql"> The SQL expression to use for the column's default constraint. </param>
        /// <param name="computedColumnSql"> The SQL expression to use to compute the column value. </param>
        /// <param name="annotatable"> The <see cref="MigrationOperation" /> to use to find any custom annotations. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void ColumnDefinition(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] Type clrType,
            [CanBeNull] string type,
            bool? unicode,
            int? maxLength,
            bool? fixedLength,
            bool rowVersion,
            bool nullable,
            [CanBeNull] object defaultValue,
            [CanBeNull] string defaultValueSql,
            [CanBeNull] string computedColumnSql,
            [NotNull] IAnnotatable annotatable,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(clrType, nameof(clrType));
            Check.NotNull(annotatable, nameof(annotatable));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" ")
                .Append(type ?? GetColumnType(schema, table, name, clrType, unicode, maxLength, fixedLength, rowVersion, model));

            builder.Append(nullable ? " NULL" : " NOT NULL");

            DefaultValue(defaultValue, defaultValueSql, builder);
        }

        /// <summary>
        ///     Gets the store/database type of a column given the provided metadata.
        /// </summary>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="table"> The table that contains the column. </param>
        /// <param name="name"> The column name. </param>
        /// <param name="clrType"> The CLR <see cref="Type" /> that the column is mapped to. </param>
        /// <param name="unicode">
        ///     Indicates whether or not the column can contain Unicode data, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="maxLength">
        ///     The maximum amount of data that the column can contain, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="rowVersion">
        ///     Indicates whether or not this column is an automatic concurrency token, such as a SQL Server timestamp/rowversion.
        /// </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <returns> The database/store type for the column. </returns>
        protected virtual string GetColumnType(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] Type clrType,
            bool? unicode,
            int? maxLength,
            bool rowVersion,
            [CanBeNull] IModel model)
            => GetColumnType(schema, table, name, clrType, unicode, maxLength, null, rowVersion, model);

        /// <summary>
        ///     Gets the store/database type of a column given the provided metadata.
        /// </summary>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="table"> The table that contains the column. </param>
        /// <param name="name"> The column name. </param>
        /// <param name="clrType"> The CLR <see cref="Type" /> that the column is mapped to. </param>
        /// <param name="unicode">
        ///     Indicates whether or not the column can contain Unicode data, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="maxLength">
        ///     The maximum amount of data that the column can contain, or <c>null</c> if this is not applicable or not specified.
        /// </param>
        /// <param name="fixedLength"> Indicates whether or not the data is constrained to fixed-length data. </param>
        /// <param name="rowVersion">
        ///     Indicates whether or not this column is an automatic concurrency token, such as a SQL Server timestamp/rowversion.
        /// </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <returns> The database/store type for the column. </returns>
        protected virtual string GetColumnType(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] Type clrType,
            bool? unicode,
            int? maxLength,
            bool? fixedLength,
            bool rowVersion,
            [CanBeNull] IModel model)
        {
            Check.NotEmpty(table, nameof(table));
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(clrType, nameof(clrType));

            var keyOrIndex = false;

            var property = FindProperty(model, schema, table, name);
            if (property != null)
            {
                if (unicode == property.IsUnicode()
                    && maxLength == property.GetMaxLength()
                    && (fixedLength ?? false) == property.Relational().IsFixedLength
                    && rowVersion == (property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate))
                {
                    return Dependencies.TypeMappingSource.FindMapping(property).StoreType;
                }

                keyOrIndex = property.IsKey() || property.IsForeignKey();
            }

            return Dependencies.TypeMappingSource.FindMapping(clrType, null, keyOrIndex, unicode, maxLength, rowVersion, fixedLength).StoreType;
        }

        /// <summary>
        ///     Generates a SQL fragment for the default constraint of a column.
        /// </summary>
        /// <param name="defaultValue"> The default value for the column. </param>
        /// <param name="defaultValueSql"> The SQL expression to use for the column's default constraint. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void DefaultValue(
            [CanBeNull] object defaultValue,
            [CanBeNull] string defaultValueSql,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            if (defaultValueSql != null)
            {
                builder
                    .Append(" DEFAULT (")
                    .Append(defaultValueSql)
                    .Append(")");
            }
            else if (defaultValue != null)
            {
                var typeMapping = Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);

                builder
                    .Append(" DEFAULT ")
                    .Append(typeMapping.GenerateSqlLiteral(defaultValue));
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for a foreign key constraint of an <see cref="AddForeignKeyOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void ForeignKeyConstraint(
            [NotNull] AddForeignKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (operation.Name != null)
            {
                builder
                    .Append("CONSTRAINT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");
            }

            builder
                .Append("FOREIGN KEY (")
                .Append(ColumnList(operation.Columns))
                .Append(") REFERENCES ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PrincipalTable, operation.PrincipalSchema));

            if (operation.PrincipalColumns != null)
            {
                builder
                    .Append(" (")
                    .Append(ColumnList(operation.PrincipalColumns))
                    .Append(")");
            }

            if (operation.OnUpdate != ReferentialAction.NoAction)
            {
                builder.Append(" ON UPDATE ");
                ForeignKeyAction(operation.OnUpdate, builder);
            }

            if (operation.OnDelete != ReferentialAction.NoAction)
            {
                builder.Append(" ON DELETE ");
                ForeignKeyAction(operation.OnDelete, builder);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for a primary key constraint of an <see cref="AddPrimaryKeyOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void PrimaryKeyConstraint(
            [NotNull] AddPrimaryKeyOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (operation.Name != null)
            {
                builder
                    .Append("CONSTRAINT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");
            }

            builder
                .Append("PRIMARY KEY ");

            IndexTraits(operation, model, builder);

            builder.Append("(")
                .Append(ColumnList(operation.Columns))
                .Append(")");
        }

        /// <summary>
        ///     Generates a SQL fragment for a unique constraint of an <see cref="AddUniqueConstraintOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void UniqueConstraint(
            [NotNull] AddUniqueConstraintOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (operation.Name != null)
            {
                builder
                    .Append("CONSTRAINT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");
            }

            builder
                .Append("UNIQUE ");

            IndexTraits(operation, model, builder);

            builder.Append("(")
                .Append(ColumnList(operation.Columns))
                .Append(")");
        }

        /// <summary>
        ///     Generates a SQL fragment for traits of an index from a <see cref="CreateIndexOperation" />,
        ///     <see cref="AddPrimaryKeyOperation" />, or <see cref="AddUniqueConstraintOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void IndexTraits(
            [NotNull] MigrationOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
        }

        /// <summary>
        ///     Generates a SQL fragment for extras (filter, included columns, options) of an index from a <see cref="CreateIndexOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void IndexOptions(
            [NotNull] CreateIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            if (!string.IsNullOrEmpty(operation.Filter))
            {
                builder
                    .Append(" WHERE ")
                    .Append(operation.Filter);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for the given referential action.
        /// </summary>
        /// <param name="referentialAction"> The referential action. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void ForeignKeyAction(
            ReferentialAction referentialAction,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            switch (referentialAction)
            {
                case ReferentialAction.Restrict:
                    builder.Append("RESTRICT");
                    break;
                case ReferentialAction.Cascade:
                    builder.Append("CASCADE");
                    break;
                case ReferentialAction.SetNull:
                    builder.Append("SET NULL");
                    break;
                case ReferentialAction.SetDefault:
                    builder.Append("SET DEFAULT");
                    break;
                default:
                    Debug.Assert(
                        referentialAction == ReferentialAction.NoAction,
                        "Unexpected value: " + referentialAction);
                    break;
            }
        }

        /// <summary>
        ///     Finds all <see cref="IEntityType" />s that are mapped to the given table.
        /// </summary>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="tableName"> The table name. </param>
        /// <returns> The list of types, which may be empty if no types are mapped to the given table. </returns>
        protected virtual IEnumerable<IEntityType> FindEntityTypes(
            [CanBeNull] IModel model,
            [CanBeNull] string schema,
            [NotNull] string tableName)
            => model?.GetEntityTypes().Where(
                t => t.Relational().TableName == tableName && t.Relational().Schema == schema && !t.IsQueryType);

        /// <summary>
        ///     <para>
        ///         Finds some <see cref="IProperty" /> mapped to the given column.
        ///     </para>
        ///     <para>
        ///         If multiple properties map to the same column, then the property returned is one chosen
        ///         arbitrarily. The model validator ensures that all properties mapped to a given column
        ///         have consistent mappings.
        ///     </para>
        /// </summary>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="schema"> The schema that contains the table, or <c>null</c> to use the default schema. </param>
        /// <param name="tableName"> The name of the table that contains the column. </param>
        /// <param name="columnName"> The column name. </param>
        /// <returns> The property found, or <c>null</c> if no property maps to the given column. </returns>
        protected virtual IProperty FindProperty(
            [CanBeNull] IModel model,
            [CanBeNull] string schema,
            [NotNull] string tableName,
            [NotNull] string columnName
            // Any property that maps to the column will work because model validator has
            // checked that all properties result in the same column definition.
        )
            => FindEntityTypes(model, schema, tableName)?.SelectMany(e => e.GetDeclaredProperties())
                .FirstOrDefault(p => p.Relational().ColumnName == columnName);

        /// <summary>
        ///     Generates a SQL fragment to terminate the SQL command.
        /// </summary>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        /// <param name="suppressTransaction">
        ///     Indicates whether or not transactions should be suppressed while executing the built command.
        /// </param>
        protected virtual void EndStatement(
            [NotNull] MigrationCommandListBuilder builder,
            bool suppressTransaction = false)
        {
            Check.NotNull(builder, nameof(builder));

            builder.EndCommand(suppressTransaction);
        }

        /// <summary>
        ///     Concatenates the given column names into a <see cref="ISqlGenerationHelper.DelimitIdentifier(string)" />
        ///     separated list.
        /// </summary>
        /// <param name="columns"> The column names. </param>
        /// <returns> The column list. </returns>
        protected virtual string ColumnList([NotNull] string[] columns)
            => string.Join(", ", columns.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier));

        /// <summary>
        ///     Checks whether or not <see cref="AddColumnOperation" /> supports the passing in the
        ///     old column, which was only added in EF Core 1.1.
        /// </summary>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <returns>
        ///     <c>True</c> If the model was generated by EF Core 1.1 or later; <c>false</c> if the model is <c>null</c>, has
        ///     no version specified, or was generated by an EF Core version prior to 1.1.
        /// </returns>
        protected virtual bool IsOldColumnSupported([CanBeNull] IModel model)
            => TryGetVersion(model, out var version) && VersionComparer.Compare(version, "1.1.0") >= 0;

        /// <summary>
        ///     Checks whether or not <see cref="RenameTableOperation" /> and <see cref="RenameSequenceOperation" /> use
        ///     the legacy behavior of setting the new name and schema to null when unchanged.
        /// </summary>
        /// <param name="model"> The target model. </param>
        /// <returns> True if the legacy behavior is used. </returns>
        protected virtual bool HasLegacyRenameOperations([CanBeNull] IModel model)
            => !TryGetVersion(model, out var version) || VersionComparer.Compare(version, "2.1.0") < 0;

        /// <summary>
        ///     Gets the product version used to generate the current migration. Providers can use this to preserve
        ///     compatibility with migrations generated using previous versions.
        /// </summary>
        /// <param name="model"> The target model. </param>
        /// <param name="version"> The version. </param>
        /// <returns> True if the version could be retrieved. </returns>
        protected virtual bool TryGetVersion(IModel model, out string version)
        {
            if (!(model?[CoreAnnotationNames.ProductVersionAnnotation] is string versionString))
            {
                version = null;

                return false;
            }

            version = versionString;

            return true;
        }
    }
}
