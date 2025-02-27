﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Glue;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Core
{
    public sealed class RenameDriver
    {
        private readonly RecordType _baseParameters;
        private readonly RecordType _renameParameters;
        private readonly INameResolver _resolver;
        private readonly Engine _engine;
        private readonly IBinderGlue _binderGlue;

        internal RenameDriver(RecordType parameters, DPath pathToRename, DName updatedName, Engine engine, INameResolver resolver, IBinderGlue binderGlue)
        {
            var segments = new Queue<DName>(pathToRename.Segments());
            Contracts.CheckParam(segments.Count > 0, nameof(parameters));

            _baseParameters = parameters;

            // After this point, _renameParameters should have at most one logical->display pair that can change in this conversion
            _renameParameters = RenameFormulaTypeHelper(parameters, segments, updatedName) as RecordType;
            _resolver = resolver;
            _engine = engine;
            _binderGlue = binderGlue;
        }

        /// <summary>
        /// Applies rename operation to <paramref name="expressionText"/>.
        /// </summary>
        /// <param name="expressionText">Expression in which to rename the parameter field.</param>
        /// <returns>Expression with rename applied, in invariant locale.</returns>
        public string ApplyRename(string expressionText)
        {
            // Ensure expression is converted to invariant before applying rename.
            var invariantExpression = _engine.GetInvariantExpression(expressionText, _baseParameters);
            var converted = ExpressionLocalizationHelper.ConvertExpression(invariantExpression, _renameParameters, BindingConfig.Default, _resolver, _binderGlue, CultureInfo.InvariantCulture, true);
            
            // Convert back to the invariant expression. All parameter values are already invariant at this point, so we pass _renameParameters, but stripped of it's DisplayNameProvider
            var strippedRenameParameters = FormulaType.Build(DType.DisableDisplayNameProviders(_renameParameters._type)) as RecordType;
            return ExpressionLocalizationHelper.ConvertExpression(converted, strippedRenameParameters, BindingConfig.Default, _resolver, _binderGlue, CultureInfo.InvariantCulture, false);
        }

        private static FormulaType RenameFormulaTypeHelper(AggregateType nestedType, Queue<DName> segments, DName updatedName)
        {
            var field = segments.Dequeue();
            if (segments.Count == 0)
            {
                // Create a display name provider with only the name in question
                var names = new Dictionary<DName, DName>
                {
                    [field] = updatedName
                };
                var newProvider = new SingleSourceDisplayNameProvider(names);

                return FormulaType.Build(DType.ReplaceDisplayNameProvider(DType.DisableDisplayNameProviders(nestedType._type), newProvider));
            }

            if (!nestedType.TryGetFieldType(field, out var fieldType) || fieldType is not AggregateType aggregateType)
            {
                // Path doesn't exist within parameters, return as is, stripping displaynameproviders
                return FormulaType.Build(DType.DisableDisplayNameProviders(nestedType._type));
            }

            var innerUpdatedType = RenameFormulaTypeHelper(aggregateType, segments, updatedName);
            var fError = false;

            // Use DType internals to swap one field type for the updated one and disable all other display names
            var dropped = DType.DisableDisplayNameProviders(nestedType._type.Drop(ref fError, DPath.Root, field));
            Contracts.Assert(!fError);

            return FormulaType.Build(dropped.Add(field, innerUpdatedType._type));
        }
    }
}
