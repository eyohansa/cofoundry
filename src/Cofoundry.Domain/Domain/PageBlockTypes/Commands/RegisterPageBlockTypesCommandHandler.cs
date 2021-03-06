﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cofoundry.Domain.CQS;
using Cofoundry.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Cofoundry.Core;

namespace Cofoundry.Domain
{
    /// <summary>
    /// Updates the page block types registered in the database using the
    /// IPageBlockDataModel types registered in the DI injector. This is typically
    /// run during the auto-update process when the application starst up.
    /// </summary>
    public class RegisterPageBlockTypesCommandHandler
        : IAsyncCommandHandler<RegisterPageBlockTypesCommand>
        , IPermissionRestrictedCommandHandler<RegisterPageBlockTypesCommand>
    {
        private readonly CofoundryDbContext _dbContext;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IPageCache _pageCache;
        private readonly IPageBlockTypeCache _blockCache;
        private readonly IEnumerable<IPageBlockTypeDataModel> _allPageBlockTypeDataModels;
        private readonly IPageBlockTypeFileNameFormatter _blockTypeFileNameFormatter;

        public RegisterPageBlockTypesCommandHandler(
            CofoundryDbContext dbContext,
            IQueryExecutor queryExecutor,
            IPageCache pageCache,
            IPageBlockTypeCache blockCache,
            IEnumerable<IPageBlockTypeDataModel> allPageBlockTypeDataModels,
            IPageBlockTypeFileNameFormatter blockTypeFileNameFormatter
            )
        {
            _dbContext = dbContext;
            _queryExecutor = queryExecutor;
            _pageCache = pageCache;
            _allPageBlockTypeDataModels = allPageBlockTypeDataModels;
            _blockCache = blockCache;
            _blockTypeFileNameFormatter = blockTypeFileNameFormatter;
        }

        public async Task ExecuteAsync(RegisterPageBlockTypesCommand command, IExecutionContext executionContext)
        {
            var dbPageBlockTypes = await _dbContext
                .PageBlockTypes
                .Include(t => t.PageBlockTemplates)
                .ToDictionaryAsync(d => d.FileName);

            DetectDuplicateBlockTypes();

            var blockTypeDataModels = _allPageBlockTypeDataModels
                .ToDictionary(m => FormatBlockTypeFileName(m));

            await DeleteBlockTypes(executionContext, dbPageBlockTypes, blockTypeDataModels);

            await UpdateBlocksAsync(executionContext, dbPageBlockTypes, blockTypeDataModels);

            await _dbContext.SaveChangesAsync();
            _pageCache.Clear();
            _blockCache.Clear();
        }

        private async Task UpdateBlocksAsync(
            IExecutionContext executionContext, 
            Dictionary<string, PageBlockType> dbPageBlockTypes, 
            Dictionary<string, IPageBlockTypeDataModel> blockTypeDataModels
            )
        {
            foreach (var model in blockTypeDataModels)
            {
                var fileName = model.Key;
                var existingBlock = dbPageBlockTypes.GetOrDefault(fileName);
                bool isUpdated = false;

                var fileDetails = await _queryExecutor.ExecuteAsync(new GetPageBlockTypeFileDetailsByFileNameQuery(fileName), executionContext);
                var name = string.IsNullOrWhiteSpace(fileDetails.Name) ? TextFormatter.PascalCaseToSentence(fileName) : fileDetails.Name;
                if (existingBlock == null)
                {
                    existingBlock = new PageBlockType();
                    existingBlock.FileName = fileName;
                    existingBlock.CreateDate = executionContext.ExecutionDate;
                    _dbContext.PageBlockTypes.Add(existingBlock);
                    isUpdated = true;
                }

                if (existingBlock.IsArchived)
                {
                    isUpdated = true;
                    existingBlock.IsArchived = false;
                }

                if (existingBlock.Name != name)
                {
                    isUpdated = true;
                    existingBlock.Name = name;
                }

                if (existingBlock.Description != fileDetails.Description)
                {
                    isUpdated = true;
                    existingBlock.Description = fileDetails.Description;
                }

                UpdateTemplates(executionContext, existingBlock, fileDetails);

                if (isUpdated)
                {
                    existingBlock.UpdateDate = executionContext.ExecutionDate;
                }
            }
        }

        private void UpdateTemplates(
            IExecutionContext executionContext, 
            PageBlockType existingBlock, 
            PageBlockTypeFileDetails fileDetails
            )
        {
            var templatesToDelete = existingBlock
                                .PageBlockTemplates
                                .Where(mt => !fileDetails.Templates.Any(t => t.FileName.Equals(mt.FileName, StringComparison.OrdinalIgnoreCase)))
                                .ToList();

            if (templatesToDelete.Any())
            {
                _dbContext.PageBlockTypeTemplates.RemoveRange(templatesToDelete);
            }

            foreach (var fileTemplate in fileDetails.Templates)
            {
                var existingTemplate = existingBlock
                    .PageBlockTemplates
                    .FirstOrDefault(t => t.FileName.Equals(fileTemplate.FileName, StringComparison.OrdinalIgnoreCase));

                if (existingTemplate == null)
                {
                    existingTemplate = new PageBlockTypeTemplate();
                    existingTemplate.CreateDate = executionContext.ExecutionDate;
                    existingBlock.PageBlockTemplates.Add(existingTemplate);
                }

                existingTemplate.FileName = fileTemplate.FileName;
                existingTemplate.Name = fileTemplate.Name;
                existingTemplate.Description = fileTemplate.Description;
            }
        }

        private async Task DeleteBlockTypes(
            IExecutionContext executionContext,
            Dictionary<string, PageBlockType> dbPageBlockTypes,
            Dictionary<string, IPageBlockTypeDataModel> pageBlockTypeDataModels)
        {
            var blockTypesToDelete = dbPageBlockTypes
                .Where(m => !pageBlockTypeDataModels.ContainsKey(m.Key) && !m.Value.IsArchived)
                .ToList();

            foreach (var blockTypeToDelete in blockTypesToDelete)
            {
                if (!await IsBlockTypeInUse(blockTypeToDelete.Value.PageBlockTypeId))
                {
                    // Clean up if it's not being used
                    _dbContext.PageBlockTypes.Remove(blockTypeToDelete.Value);
                }
                else
                {
                    // Else archive to allow for later clean-up or migration
                    blockTypeToDelete.Value.IsArchived = true;
                    blockTypeToDelete.Value.UpdateDate = executionContext.ExecutionDate;
                }
            }
        }

        /// <remarks>
        /// We could potentially use namespacing here, but let's leave it out for
        /// now because it would throw up some issues - e.g. how would you reference it 
        /// by string, how would you ensure the template file was in a unique path?
        /// </remarks>
        private void DetectDuplicateBlockTypes()
        {
            var duplicateBlockTypeDefinitions = _allPageBlockTypeDataModels
                    .GroupBy(m => FormatBlockTypeFileName(m))
                    .Where(m => m.Count() > 1)
                    .FirstOrDefault();

            if (!EnumerableHelper.IsNullOrEmpty(duplicateBlockTypeDefinitions))
            {
                var blockTypes = string.Join(", ", duplicateBlockTypeDefinitions.Select(t => t.GetType().FullName));
                throw new PageBlockTypeRegistrationException(
                    $"Duplicate page block type '{ duplicateBlockTypeDefinitions.Key }' detected. Conflicting types: { blockTypes }");
            }
        }

        private string FormatBlockTypeFileName(IPageBlockTypeDataModel m)
        {
            return _blockTypeFileNameFormatter.FormatFromDataModelType(m.GetType());
        }

        private Task<bool> IsBlockTypeInUse(int pageBlockTypeId)
        {
            var isInUse = _dbContext
                .PageBlockTypes
                .AsNoTracking()
                .Where(m => m.PageBlockTypeId == pageBlockTypeId)
                .AnyAsync(m => m.PageVersionBlocks.Any() || m.CustomEntityVersionPageBlocks.Any());

            return isInUse;
        }

        #region permissions

        public IEnumerable<IPermissionApplication> GetPermissions(RegisterPageBlockTypesCommand command)
        {
            // Permissions are tied to the page templating system

            yield return new PageTemplateCreatePermission();
            yield return new PageTemplateUpdatePermission();
            yield return new PageTemplateDeletePermission();
        }

        #endregion
    }
}
