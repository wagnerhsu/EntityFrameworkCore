// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Design.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class DatabaseOperations
    {
        private readonly IOperationReporter _reporter;
        private readonly string _projectDir;
        private readonly string _rootNamespace;
        private readonly string _language;
        private readonly DesignTimeServicesBuilder _servicesBuilder;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public DatabaseOperations(
            [NotNull] IOperationReporter reporter,
            [NotNull] Assembly assembly,
            [NotNull] Assembly startupAssembly,
            [NotNull] string projectDir,
            [NotNull] string rootNamespace,
            [CanBeNull] string language,
            [NotNull] string[] args)
        {
            Check.NotNull(reporter, nameof(reporter));
            Check.NotNull(startupAssembly, nameof(startupAssembly));
            Check.NotNull(projectDir, nameof(projectDir));
            Check.NotNull(rootNamespace, nameof(rootNamespace));
            Check.NotNull(args, nameof(args));

            _reporter = reporter;
            _projectDir = projectDir;
            _rootNamespace = rootNamespace;
            _language = language;

            _servicesBuilder = new DesignTimeServicesBuilder(assembly, startupAssembly, reporter, args);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual SavedModelFiles ScaffoldContext(
            [NotNull] string provider,
            [NotNull] string connectionString,
            [CanBeNull] string outputDir,
            [CanBeNull] string outputContextDir,
            [CanBeNull] string dbContextClassName,
            [NotNull] IEnumerable<string> schemas,
            [NotNull] IEnumerable<string> tables,
            bool useDataAnnotations,
            bool overwriteFiles,
            bool useDatabaseNames)
        {
            Check.NotEmpty(provider, nameof(provider));
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(schemas, nameof(schemas));
            Check.NotNull(tables, nameof(tables));

            outputDir = outputDir != null
                ? Path.GetFullPath(Path.Combine(_projectDir, outputDir))
                : _projectDir;

            outputContextDir = outputContextDir != null
                ? Path.GetFullPath(Path.Combine(_projectDir, outputContextDir))
                : outputDir;

            var services = _servicesBuilder.Build(provider);

            var scaffolder = services.GetRequiredService<IReverseEngineerScaffolder>();

            var @namespace = _rootNamespace;

            var subNamespace = SubnamespaceFromOutputPath(_projectDir, outputDir);
            if (!string.IsNullOrEmpty(subNamespace))
            {
                @namespace += "." + subNamespace;
            }

            var scaffoldedModel = scaffolder.ScaffoldModel(
                connectionString,
                tables,
                schemas,
                @namespace,
                _language,
                MakeDirRelative(outputDir, outputContextDir),
                dbContextClassName,
                new ModelReverseEngineerOptions
                {
                    UseDatabaseNames = useDatabaseNames
                },
                new ModelCodeGenerationOptions
                {
                    UseDataAnnotations = useDataAnnotations
                });

            return scaffolder.Save(
                scaffoldedModel,
                outputDir,
                overwriteFiles);
        }

        // if outputDir is a subfolder of projectDir, then use each subfolder as a subnamespace
        // --output-dir $(projectFolder)/A/B/C
        // => "namespace $(rootnamespace).A.B.C"
        private static string SubnamespaceFromOutputPath(string projectDir, string outputDir)
        {
            if (!outputDir.StartsWith(projectDir, StringComparison.Ordinal))
            {
                return null;
            }

            var subPath = outputDir.Substring(projectDir.Length);

            return !string.IsNullOrWhiteSpace(subPath)
                ? string.Join(".", subPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                : null;
        }

        private static string MakeDirRelative(string root, string path)
        {
            var relativeUri = new Uri(NormalizeDir(root)).MakeRelativeUri(new Uri(NormalizeDir(path)));

            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizeDir(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var last = path[path.Length - 1];
            return last == Path.DirectorySeparatorChar
                || last == Path.AltDirectorySeparatorChar
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
