// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Update.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Migrations
{
    /// <summary>
    ///     SQL Server-specific implementation of <see cref="MigrationsSqlGenerator" />.
    /// </summary>
    public class SqlServerMigrationsSqlGenerator : MigrationsSqlGenerator
    {
        private readonly IMigrationsAnnotationProvider _migrationsAnnotations;

        private IReadOnlyList<MigrationOperation> _operations;
        private int _variableCounter;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        /// <param name="migrationsAnnotations"> Provider-specific Migrations annotations to use. </param>
        public SqlServerMigrationsSqlGenerator(
            [NotNull] MigrationsSqlGeneratorDependencies dependencies,
            [NotNull] IMigrationsAnnotationProvider migrationsAnnotations)
            : base(dependencies) => _migrationsAnnotations = migrationsAnnotations;

        /// <summary>
        ///     Generates commands from a list of operations.
        /// </summary>
        /// <param name="operations"> The operations. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <returns> The list of commands to be executed or scripted. </returns>
        public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations, IModel model)
        {
            _operations = operations;
            try
            {
                return base.Generate(operations, model);
            }
            finally
            {
                _operations = null;
            }
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
        protected override void Generate(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var dropDatabaseOperation = operation as SqlServerDropDatabaseOperation;
            if (operation is SqlServerCreateDatabaseOperation createDatabaseOperation)
            {
                Generate(createDatabaseOperation, model, builder);
            }
            else if (dropDatabaseOperation != null)
            {
                Generate(dropDatabaseOperation, model, builder);
            }
            else
            {
                base.Generate(operation, model, builder);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AddColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="AddColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            AddColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AddForeignKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AddPrimaryKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterColumnOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AlterColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            IEnumerable<IIndex> indexesToRebuild = null;
            var property = FindProperty(model, operation.Schema, operation.Table, operation.Name);

            if (operation.ComputedColumnSql != null)
            {
                var dropColumnOperation = new DropColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name
                };
                if (property != null)
                {
                    dropColumnOperation.AddAnnotations(_migrationsAnnotations.ForRemove(property));
                }

                var addColumnOperation = new AddColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name,
                    ClrType = operation.ClrType,
                    ColumnType = operation.ColumnType,
                    IsUnicode = operation.IsUnicode,
                    MaxLength = operation.MaxLength,
                    IsRowVersion = operation.IsRowVersion,
                    IsNullable = operation.IsNullable,
                    DefaultValue = operation.DefaultValue,
                    DefaultValueSql = operation.DefaultValueSql,
                    ComputedColumnSql = operation.ComputedColumnSql,
                    IsFixedLength = operation.IsFixedLength
                };
                addColumnOperation.AddAnnotations(operation.GetAnnotations());

                // TODO: Use a column rebuild instead
                indexesToRebuild = GetIndexesToRebuild(property, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
                Generate(dropColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                Generate(addColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                CreateIndexes(indexesToRebuild, builder);
                builder.EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));

                return;
            }

            var narrowed = false;
            if (IsOldColumnSupported(model))
            {
                var valueGenerationStrategy = operation[
                    SqlServerAnnotationNames.ValueGenerationStrategy] as SqlServerValueGenerationStrategy?;
                var identity = valueGenerationStrategy == SqlServerValueGenerationStrategy.IdentityColumn;
                var oldValueGenerationStrategy = operation.OldColumn[
                    SqlServerAnnotationNames.ValueGenerationStrategy] as SqlServerValueGenerationStrategy?;
                var oldIdentity = oldValueGenerationStrategy == SqlServerValueGenerationStrategy.IdentityColumn;
                if (identity != oldIdentity)
                {
                    throw new InvalidOperationException(SqlServerStrings.AlterIdentityColumn);
                }

                var type = operation.ColumnType
                           ?? GetColumnType(
                               operation.Schema,
                               operation.Table,
                               operation.Name,
                               operation.ClrType,
                               operation.IsUnicode,
                               operation.MaxLength,
                               operation.IsFixedLength,
                               operation.IsRowVersion,
                               model);
                var oldType = operation.OldColumn.ColumnType
                              ?? GetColumnType(
                                  operation.Schema,
                                  operation.Table,
                                  operation.Name,
                                  operation.OldColumn.ClrType,
                                  operation.OldColumn.IsUnicode,
                                  operation.OldColumn.MaxLength,
                                  operation.OldColumn.IsFixedLength,
                                  operation.OldColumn.IsRowVersion,
                                  model);
                narrowed = type != oldType || !operation.IsNullable && operation.OldColumn.IsNullable;
            }

            if (narrowed)
            {
                indexesToRebuild = GetIndexesToRebuild(property, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
            }

            DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ALTER COLUMN ");

            ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                operation.IsFixedLength,
                operation.IsRowVersion,
                operation.IsNullable,
                /*defaultValue:*/ null,
                /*defaultValueSql:*/ null,
                operation.ComputedColumnSql,
                /*identity:*/ false,
                operation,
                model,
                builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            if (operation.DefaultValue != null
                || operation.DefaultValueSql != null)
            {
                builder
                    .Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" ADD");
                DefaultValue(operation.DefaultValue, operation.DefaultValueSql, builder);
                builder
                    .Append(" FOR ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            if (narrowed)
            {
                CreateIndexes(indexesToRebuild, builder);
            }

            builder.EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameIndexOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (string.IsNullOrEmpty(operation.Table))
            {
                throw new InvalidOperationException(SqlServerStrings.IndexTableRequired);
            }

            Rename(
                Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema) +
                "." +
                Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name),
                operation.NewName,
                "INDEX",
                builder);
            builder.EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameSequenceOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(RenameSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var name = operation.Name;
            if (operation.NewName != null
                && operation.NewName != name)
            {
                Rename(
                    Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema),
                    operation.NewName,
                    builder);

                name = operation.NewName;
            }

            if (operation.NewSchema != operation.Schema
                && (operation.NewSchema != null
                    || !HasLegacyRenameOperations(model)))
            {
                Transfer(operation.NewSchema, operation.Schema, name, builder);
            }

            builder.EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RestartSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RestartSequenceOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" RESTART WITH ")
                .Append(IntegerConstant(operation.StartValue))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            CreateTableOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            var memoryOptimized = IsMemoryOptimized(operation);
            if (memoryOptimized)
            {
                builder.AppendLine();
                using (builder.Indent())
                {
                    builder.AppendLine("WITH");
                    using (builder.Indent())
                    {
                        builder.Append("(MEMORY_OPTIMIZED = ON)");
                    }
                }
            }

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: memoryOptimized);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameTableOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameTableOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var name = operation.Name;
            if (operation.NewName != null
                && operation.NewName != name)
            {
                Rename(
                    Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema),
                    operation.NewName,
                    builder);

                name = operation.NewName;
            }

            if (operation.NewSchema != operation.Schema
                && (operation.NewSchema != null
                    || !HasLegacyRenameOperations(model)))
            {
                Transfer(operation.NewSchema, operation.Schema, name, builder);
            }

            builder.EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(DropTableOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Name));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateIndexOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            CreateIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="CreateIndexOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            CreateIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var nullableColumns = operation.Columns
                .Where(
                    c =>
                    {
                        var property = FindProperty(model, operation.Schema, operation.Table, c);

                        return property?.IsColumnNullable() != false;
                    })
                .ToList();

            var memoryOptimized = IsMemoryOptimized(operation, model, operation.Schema, operation.Table);
            if (memoryOptimized)
            {
                builder.Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" ADD INDEX ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");

                if (operation.IsUnique
                    && nullableColumns.Count == 0)
                {
                    builder.Append("UNIQUE ");
                }

                IndexTraits(operation, model, builder);

                builder
                    .Append("(")
                    .Append(ColumnList(operation.Columns))
                    .Append(")");
            }
            else
            {
                base.Generate(operation, model, builder, terminate: false);

                if (operation.Filter == null
                    && UseLegacyIndexFilters(model))
                {
                    var clustered = operation[SqlServerAnnotationNames.Clustered] as bool?;
                    if (operation.IsUnique
                        && (clustered != true)
                        && nullableColumns.Count != 0)
                    {
                        builder.Append(" WHERE ");
                        for (var i = 0; i < nullableColumns.Count; i++)
                        {
                            if (i != 0)
                            {
                                builder.Append(" AND ");
                            }

                            builder
                                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(nullableColumns[i]))
                                .Append(" IS NOT NULL");
                        }
                    }
                }
            }

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: memoryOptimized);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            DropPrimaryKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="EnsureSchemaOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(EnsureSchemaOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (string.Equals(operation.Name, "DBO", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            builder
                .Append("IF SCHEMA_ID(")
                .Append(stringTypeMapping.GenerateSqlLiteral(operation.Name))
                .Append(") IS NULL EXEC(")
                .Append(
                    stringTypeMapping.GenerateSqlLiteral(
                        "CREATE SCHEMA " +
                        Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name) +
                        Dependencies.SqlGenerationHelper.StatementTerminator))
                .Append(")")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateSequenceOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            CreateSequenceOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("CREATE SEQUENCE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

            if (operation.ClrType != typeof(long))
            {
                var typeMapping = Dependencies.TypeMappingSource.GetMapping(operation.ClrType);

                builder
                    .Append(" AS ")
                    .Append(typeMapping.StoreType);
            }

            builder
                .Append(" START WITH ")
                .Append(IntegerConstant(operation.StartValue));

            SequenceOptions(operation, model, builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="SqlServerCreateDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] SqlServerCreateDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("CREATE DATABASE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            if (!string.IsNullOrEmpty(operation.FileName))
            {
                var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

                var fileName = ExpandFileName(operation.FileName);
                var name = Path.GetFileNameWithoutExtension(fileName);

                var logFileName = Path.ChangeExtension(fileName, ".ldf");
                var logName = name + "_log";

                // Match default naming behavior of SQL Server
                logFileName = logFileName.Insert(logFileName.Length - ".ldf".Length, "_log");

                builder
                    .AppendLine()
                    .Append("ON (NAME = ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(name))
                    .Append(", FILENAME = ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(fileName))
                    .Append(")")
                    .AppendLine()
                    .Append("LOG ON (NAME = ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(logName))
                    .Append(", FILENAME = ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(logFileName))
                    .Append(")");
            }

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true)
                .AppendLine("IF SERVERPROPERTY('EngineEdition') <> 5")
                .AppendLine("BEGIN");

            using (builder.Indent())
            {
                builder
                    .Append("ALTER DATABASE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" SET READ_COMMITTED_SNAPSHOT ON")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            builder
                .Append("END")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

        private static string ExpandFileName(string fileName)
        {
            Check.NotNull(fileName, nameof(fileName));

            if (fileName.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
            {
                var dataDirectory = AppDomain.CurrentDomain.GetData("DataDirectory") as string;
                if (string.IsNullOrEmpty(dataDirectory))
                {
                    dataDirectory = AppDomain.CurrentDomain.BaseDirectory;
                }

                fileName = Path.Combine(dataDirectory, fileName.Substring("|DataDirectory|".Length));
            }

            return Path.GetFullPath(fileName);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="SqlServerDropDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            [NotNull] SqlServerDropDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .AppendLine("IF SERVERPROPERTY('EngineEdition') <> 5")
                .AppendLine("BEGIN");

            using (builder.Indent())
            {
                builder
                    .Append("ALTER DATABASE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" SET SINGLE_USER WITH ROLLBACK IMMEDIATE")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            builder
                .Append("END")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true)
                .Append("DROP DATABASE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AlterDatabaseOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (!IsMemoryOptimized(operation))
            {
                return;
            }

            builder.AppendLine("IF SERVERPROPERTY('IsXTPSupported') = 1 AND SERVERPROPERTY('EngineEdition') <> 5");
            using (builder.Indent())
            {
                builder
                    .AppendLine("BEGIN")
                    .AppendLine("IF NOT EXISTS (");
                using (builder.Indent())
                {
                    builder
                        .Append("SELECT 1 FROM [sys].[filegroups] [FG] ")
                        .Append("JOIN [sys].[database_files] [F] ON [FG].[data_space_id] = [F].[data_space_id] ")
                        .AppendLine("WHERE [FG].[type] = N'FX' AND [F].[type] = 2)");
                }

                using (builder.Indent())
                {
                    builder
                        .AppendLine("BEGIN")
                        .AppendLine("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF;")
                        .AppendLine("DECLARE @db_name NVARCHAR(MAX) = DB_NAME();")
                        .AppendLine("DECLARE @fg_name NVARCHAR(MAX);")
                        .AppendLine("SELECT TOP(1) @fg_name = [name] FROM [sys].[filegroups] WHERE [type] = N'FX';")
                        .AppendLine()
                        .AppendLine("IF @fg_name IS NULL");

                    using (builder.Indent())
                    {
                        builder
                            .AppendLine("BEGIN")
                            .AppendLine("SET @fg_name = @db_name + N'_MODFG';")
                            .AppendLine("EXEC(N'ALTER DATABASE CURRENT ADD FILEGROUP [' + @fg_name + '] CONTAINS MEMORY_OPTIMIZED_DATA;');")
                            .AppendLine("END");
                    }

                    builder
                        .AppendLine()
                        .AppendLine("DECLARE @path NVARCHAR(MAX);")
                        .Append("SELECT TOP(1) @path = [physical_name] FROM [sys].[database_files] ")
                        .AppendLine("WHERE charindex('\\', [physical_name]) > 0 ORDER BY [file_id];")
                        .AppendLine("IF (@path IS NULL)")
                        .IncrementIndent().AppendLine("SET @path = '\\' + @db_name;").DecrementIndent()
                        .AppendLine()
                        .AppendLine("DECLARE @filename NVARCHAR(MAX) = right(@path, charindex('\\', reverse(@path)) - 1);")
                        .AppendLine("SET @filename = REPLACE(left(@filename, len(@filename) - charindex('.', reverse(@filename))), '''', '''''') + N'_MOD';")
                        .AppendLine("DECLARE @new_path NVARCHAR(MAX) = REPLACE(CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(MAX)), '''', '''''') + @filename;")
                        .AppendLine()
                        .AppendLine("EXEC(N'");

                    using (builder.Indent())
                    {
                        builder
                            .AppendLine("ALTER DATABASE CURRENT")
                            .AppendLine("ADD FILE (NAME=''' + @filename + ''', filename=''' + @new_path + ''')")
                            .AppendLine("TO FILEGROUP [' + @fg_name + '];')");
                    }

                    builder.AppendLine("END");
                }

                builder.AppendLine("END");
            }

            builder.AppendLine()
                .AppendLine("IF SERVERPROPERTY('IsXTPSupported') = 1")
                .AppendLine("EXEC(N'");
            using (builder.Indent())
            {
                builder
                    .AppendLine("ALTER DATABASE CURRENT")
                    .AppendLine("SET MEMORY_OPTIMIZED_ELEVATE_TO_SNAPSHOT ON;')");
            }

            builder.EndCommand(suppressTransaction: true);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterTableOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(AlterTableOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            if (IsMemoryOptimized(operation)
                ^ IsMemoryOptimized(operation.OldTable))
            {
                throw new InvalidOperationException(SqlServerStrings.AlterMemoryOptimizedTable);
            }

            base.Generate(operation, model, builder);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(DropForeignKeyOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropIndexOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            DropIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropIndexOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected virtual void Generate(
            [NotNull] DropIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var memoryOptimized = IsMemoryOptimized(operation, model, operation.Schema, operation.Table);
            if (memoryOptimized)
            {
                builder
                    .Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" DROP INDEX ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            }
            else
            {
                builder
                    .Append("DROP INDEX ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ON ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
            }

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: memoryOptimized);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            DropColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        /// <summary>
        ///     Builds commands for the given <see cref="DropColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            DropColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameColumnOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            Rename(
                Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema) +
                "." +
                Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name),
                operation.NewName,
                "COLUMN",
                builder);
            builder.EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="SqlOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(SqlOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var batches = Regex.Split(
                Regex.Replace(
                    operation.Sql,
                    @"\\\r?\n",
                    string.Empty,
                    default,
                    TimeSpan.FromMilliseconds(1000.0)),
                @"^\s*(GO[ \t]+[0-9]+|GO)(?:\s+|$)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromMilliseconds(1000.0));
            for (var i = 0; i < batches.Length; i++)
            {
                if (batches[i].StartsWith("GO", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(batches[i]))
                {
                    continue;
                }

                var count = 1;
                if (i != batches.Length - 1
                    && batches[i + 1].StartsWith("GO", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(
                        batches[i + 1], "([0-9]+)",
                        default,
                        TimeSpan.FromMilliseconds(1000.0));
                    if (match.Success)
                    {
                        count = int.Parse(match.Value);
                    }
                }

                for (var j = 0; j < count; j++)
                {
                    builder.Append(batches[i]);

                    if (i == batches.Length - 1)
                    {
                        builder.AppendLine();
                    }

                    EndStatement(builder, operation.SuppressTransaction);
                }
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="InsertDataOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            InsertDataOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            GenerateIdentityInsert(builder, operation, on: true);

            var sqlBuilder = new StringBuilder();
            ((SqlServerUpdateSqlGenerator)Dependencies.UpdateSqlGenerator).AppendBulkInsertOperation(
                sqlBuilder,
                operation.GenerateModificationCommands(model).ToList(),
                0);

            builder.Append(sqlBuilder.ToString());

            GenerateIdentityInsert(builder, operation, on: false);

            builder.EndCommand();
        }

        private void GenerateIdentityInsert(MigrationCommandListBuilder builder, InsertDataOperation operation, bool on)
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            builder
                .Append("IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE")
                .Append(" [name] IN (")
                .Append(string.Join(", ", operation.Columns.Select(stringTypeMapping.GenerateSqlLiteral)))
                .Append(") AND [object_id] = OBJECT_ID(")
                .Append(
                    stringTypeMapping.GenerateSqlLiteral(
                        Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema)))
                .AppendLine("))");

            using (builder.Indent())
            {
                builder
                    .Append("SET IDENTITY_INSERT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(on ? " ON" : " OFF")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

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
        protected override void SequenceOptions(
            string schema,
            string name,
            int increment,
            long? minimumValue,
            long? maximumValue,
            bool cycle,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(increment, nameof(increment));
            Check.NotNull(cycle, nameof(cycle));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append(" INCREMENT BY ")
                .Append(IntegerConstant(increment));

            if (minimumValue.HasValue)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(IntegerConstant(minimumValue.Value));
            }
            else
            {
                builder.Append(" NO MINVALUE");
            }

            if (maximumValue.HasValue)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(IntegerConstant(maximumValue.Value));
            }
            else
            {
                builder.Append(" NO MAXVALUE");
            }

            builder.Append(cycle ? " CYCLE" : " NO CYCLE");
        }

        /// <summary>
        ///     Generates a SQL fragment for a column definition in an <see cref="AddColumnOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void ColumnDefinition(AddColumnOperation operation, IModel model, MigrationCommandListBuilder builder)
            => ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                operation.IsFixedLength,
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
        protected override void ColumnDefinition(
            string schema,
            string table,
            string name,
            Type clrType,
            string type,
            bool? unicode,
            int? maxLength,
            bool? fixedLength,
            bool rowVersion,
            bool nullable,
            object defaultValue,
            string defaultValueSql,
            string computedColumnSql,
            IAnnotatable annotatable,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var valueGenerationStrategy = annotatable[
                SqlServerAnnotationNames.ValueGenerationStrategy] as SqlServerValueGenerationStrategy?;

            ColumnDefinition(
                schema,
                table,
                name,
                clrType,
                type,
                unicode,
                maxLength,
                fixedLength,
                rowVersion,
                nullable,
                defaultValue,
                defaultValueSql,
                computedColumnSql,
                valueGenerationStrategy == SqlServerValueGenerationStrategy.IdentityColumn,
                annotatable,
                model,
                builder);
        }

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
        /// <param name="identity"> Indicates whether or not the column is an Identity column. </param>
        /// <param name="annotatable"> The <see cref="MigrationOperation" /> to use to find any custom annotations. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        [Obsolete("Use the overload with most parameters")]
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
            bool identity,
            [NotNull] IAnnotatable annotatable,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
            => ColumnDefinition(
                schema, table, name, clrType, type, unicode, maxLength, null,
                rowVersion, nullable, defaultValue, defaultValueSql, computedColumnSql, identity, annotatable, model, builder);

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
        /// <param name="identity"> Indicates whether or not the column is an Identity column. </param>
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
            bool identity,
            [NotNull] IAnnotatable annotatable,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(clrType, nameof(clrType));
            Check.NotNull(annotatable, nameof(annotatable));
            Check.NotNull(builder, nameof(builder));

            if (computedColumnSql != null)
            {
                builder
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                    .Append(" AS ")
                    .Append(computedColumnSql);

                return;
            }

            base.ColumnDefinition(
                schema,
                table,
                name,
                clrType,
                type,
                unicode,
                maxLength,
                fixedLength,
                rowVersion,
                nullable,
                identity
                    ? null
                    : defaultValue,
                defaultValueSql,
                computedColumnSql,
                annotatable,
                model,
                builder);

            if (identity)
            {
                builder.Append(" IDENTITY");
            }
        }

        /// <summary>
        ///     Generates a rename.
        /// </summary>
        /// <param name="name"> The old name. </param>
        /// <param name="newName"> The new name. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Rename(
            [NotNull] string name,
            [NotNull] string newName,
            [NotNull] MigrationCommandListBuilder builder) => Rename(name, newName, /*type:*/ null, builder);

        /// <summary>
        ///     Generates a rename.
        /// </summary>
        /// <param name="name"> The old name. </param>
        /// <param name="newName"> The new name. </param>
        /// <param name="type"> If not <c>null</c>, then appends literal for type of object being renamed (e.g. column or index.) </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Rename(
            [NotNull] string name,
            [NotNull] string newName,
            [CanBeNull] string type,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotEmpty(newName, nameof(newName));
            Check.NotNull(builder, nameof(builder));

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            builder
                .Append("EXEC sp_rename ")
                .Append(stringTypeMapping.GenerateSqlLiteral(name))
                .Append(", ")
                .Append(stringTypeMapping.GenerateSqlLiteral(newName));

            if (type != null)
            {
                builder
                    .Append(", ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(type));
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>
        ///     Generates a transfer from one schema to another..
        /// </summary>
        /// <param name="newSchema"> The schema to transfer to. </param>
        /// <param name="schema"> The schema to transfer from. </param>
        /// <param name="name"> The name of the item to transfer. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Transfer(
            [CanBeNull] string newSchema,
            [CanBeNull] string schema,
            [NotNull] string name,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(builder, nameof(builder));

            if (newSchema == null)
            {
                var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

                builder
                    .AppendLine("DECLARE @defaultSchema sysname = SCHEMA_NAME();")
                    .Append("EXEC(")
                    .Append("N'ALTER SCHEMA [' + @defaultSchema + ")
                    .Append(
                        stringTypeMapping.GenerateSqlLiteral(
                            "] TRANSFER " + Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema) + ";"))
                    .AppendLine(");");
            }
            else
            {
                builder
                    .Append("ALTER SCHEMA ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(newSchema))
                    .Append(" TRANSFER ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for traits of an index from a <see cref="CreateIndexOperation" />,
        ///     <see cref="AddPrimaryKeyOperation" />, or <see cref="AddUniqueConstraintOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void IndexTraits(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var clustered = operation[SqlServerAnnotationNames.Clustered] as bool?;
            if (clustered.HasValue)
            {
                builder.Append(clustered.Value ? "CLUSTERED " : "NONCLUSTERED ");
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for extras (filter, included columns, options) of an index from a <see cref="CreateIndexOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void IndexOptions(CreateIndexOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            if (operation[SqlServerAnnotationNames.Include] is IReadOnlyList<string> includeProperties && includeProperties.Count > 0)
            {
                builder.Append(" INCLUDE (");
                for (var i = 0; i < includeProperties.Count; i++)
                {
                    builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(includeProperties[i]));

                    if (i != includeProperties.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }
                builder.Append(")");
            }

            base.IndexOptions(operation, model, builder);
        }

        /// <summary>
        ///     Generates a SQL fragment for the given referential action.
        /// </summary>
        /// <param name="referentialAction"> The referential action. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void ForeignKeyAction(ReferentialAction referentialAction, MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            if (referentialAction == ReferentialAction.Restrict)
            {
                builder.Append("NO ACTION");
            }
            else
            {
                base.ForeignKeyAction(referentialAction, builder);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment to drop default constraints for a column.
        /// </summary>
        /// <param name="schema"> The schema that contains the table. </param>
        /// <param name="tableName"> The table that contains the column.</param>
        /// <param name="columnName"> The column. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void DropDefaultConstraint(
            [CanBeNull] string schema,
            [NotNull] string tableName,
            [NotNull] string columnName,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(tableName, nameof(tableName));
            Check.NotEmpty(columnName, nameof(columnName));
            Check.NotNull(builder, nameof(builder));

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            var variable = "@var" + _variableCounter++;

            builder
                .Append("DECLARE ")
                .Append(variable)
                .AppendLine(" sysname;")
                .Append("SELECT ")
                .Append(variable)
                .AppendLine(" = [d].[name]")
                .AppendLine("FROM [sys].[default_constraints] [d]")
                .AppendLine("INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]")
                .Append("WHERE ([d].[parent_object_id] = OBJECT_ID(")
                .Append(
                    stringTypeMapping.GenerateSqlLiteral(
                        Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName, schema)))
                .Append(") AND [c].[name] = ")
                .Append(stringTypeMapping.GenerateSqlLiteral(columnName))
                .AppendLine(");")
                .Append("IF ")
                .Append(variable)
                .Append(" IS NOT NULL EXEC(")
                .Append(
                    stringTypeMapping.GenerateSqlLiteral(
                        "ALTER TABLE " +
                        Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName, schema) +
                        " DROP CONSTRAINT ["))
                .Append(" + ")
                .Append(variable)
                .Append(" + ']")
                .Append(Dependencies.SqlGenerationHelper.StatementTerminator)
                .Append("')")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>
        ///     Gets the list of indexes that need to be rebuilt when the given property is changing.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="currentOperation"> The operation which may require a rebuild. </param>
        /// <returns> The list of indexes affected. </returns>
        protected virtual IEnumerable<IIndex> GetIndexesToRebuild(
            [CanBeNull] IProperty property,
            [NotNull] MigrationOperation currentOperation)
        {
            Check.NotNull(currentOperation, nameof(currentOperation));

            if (property == null)
            {
                yield break;
            }

            var createIndexOperations = _operations.SkipWhile(o => o != currentOperation).Skip(1)
                .OfType<CreateIndexOperation>().ToList();
            foreach (var index in property.DeclaringEntityType.GetIndexes().Concat(property.DeclaringEntityType.GetDerivedTypes().SelectMany(et => et.GetDeclaredIndexes())))
            {
                var indexName = index.Relational().Name;
                if (createIndexOperations.Any(o => o.Name == indexName))
                {
                    continue;
                }

                if (index.Properties.Any(p => p == property))
                {
                    yield return index;
                }
                else if (index.SqlServer().IncludeProperties is IReadOnlyList<string> includeProperties)
                {
                    if (includeProperties.Contains(property.Name))
                    {
                        yield return index;
                    }
                }
            }
        }

        /// <summary>
        ///     Generates SQL to drop the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to drop. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void DropIndexes(
            [NotNull] IEnumerable<IIndex> indexes,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(indexes, nameof(indexes));
            Check.NotNull(builder, nameof(builder));

            foreach (var index in indexes)
            {
                var operation = new DropIndexOperation
                {
                    Schema = index.DeclaringEntityType.Relational().Schema,
                    Table = index.DeclaringEntityType.Relational().TableName,
                    Name = index.Relational().Name
                };
                operation.AddAnnotations(_migrationsAnnotations.ForRemove(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Generates SQL to create the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to create. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void CreateIndexes(
            [NotNull] IEnumerable<IIndex> indexes,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(indexes, nameof(indexes));
            Check.NotNull(builder, nameof(builder));

            foreach (var index in indexes)
            {
                var operation = new CreateIndexOperation
                {
                    IsUnique = index.IsUnique,
                    Name = index.Relational().Name,
                    Schema = index.DeclaringEntityType.Relational().Schema,
                    Table = index.DeclaringEntityType.Relational().TableName,
                    Columns = index.Properties.Select(p => p.Relational().ColumnName).ToArray(),
                    Filter = index.Relational().Filter
                };
                operation.AddAnnotations(_migrationsAnnotations.For(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Checks whether or not <see cref="CreateIndexOperation" /> should have a filter generated for it by
        ///     Migrations.
        /// </summary>
        /// <param name="model"> The target model. </param>
        /// <returns> True if a filter should be generated. </returns>
        protected virtual bool UseLegacyIndexFilters([CanBeNull] IModel model)
            => !TryGetVersion(model, out var version) || VersionComparer.Compare(version, "2.0.0") < 0;

        private string IntegerConstant(long value)
            => string.Format(CultureInfo.InvariantCulture, "{0}", value);

        private bool IsMemoryOptimized(Annotatable annotatable, IModel model, string schema, string tableName)
            => annotatable[SqlServerAnnotationNames.MemoryOptimized] as bool?
               ?? FindEntityTypes(model, schema, tableName)?.Any(t => t.SqlServer().IsMemoryOptimized) == true;

        private static bool IsMemoryOptimized(Annotatable annotatable)
            => annotatable[SqlServerAnnotationNames.MemoryOptimized] as bool? == true;
    }
}
