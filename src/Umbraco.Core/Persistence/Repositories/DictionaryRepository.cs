using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.Caching;
using Umbraco.Core.Persistence.Factories;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.Relators;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    /// <summary>
    /// Represents a repository for doing CRUD operations for <see cref="DictionaryItem"/>
    /// </summary>
    internal class DictionaryRepository : PetaPocoRepositoryBase<int, IDictionaryItem>, IDictionaryRepository
    {
        private readonly ILanguageRepository _languageRepository;

        public DictionaryRepository(IUnitOfWork work, ILanguageRepository languageRepository) : base(work)
        {
            _languageRepository = languageRepository;
        }

        public DictionaryRepository(IUnitOfWork work, IRepositoryCacheProvider cache, ILanguageRepository languageRepository)
            : base(work, cache)
        {
            _languageRepository = languageRepository;
        }

        #region Overrides of RepositoryBase<int,DictionaryItem>

        protected override IDictionaryItem PerformGet(int id)
        {
            var sql = GetBaseQuery(false);
            sql.Where(GetBaseWhereClause(), new { Id = id });

            var dto = Database.Fetch<DictionaryDto, LanguageTextDto, DictionaryDto>(new DictionaryLanguageTextRelator().Map, sql).FirstOrDefault();
            if (dto == null)
                return null;

            var factory = new DictionaryItemFactory();
            var entity = factory.BuildEntity(dto);

            var list = new List<IDictionaryTranslation>();
            foreach (var textDto in dto.LanguageTextDtos)
            {
                var language = _languageRepository.Get(textDto.LanguageId);
                var translationFactory = new DictionaryTranslationFactory(dto.Id, language);
                list.Add(translationFactory.BuildEntity(textDto));
            }
            entity.Translations = list;

            ((ICanBeDirty)entity).ResetDirtyProperties();

            return entity;
        }

        protected override IEnumerable<IDictionaryItem> PerformGetAll(params int[] ids)
        {
            if (ids.Any())
            {
                foreach (var id in ids)
                {
                    yield return Get(id);
                }
            }
            else
            {
                var dtos = Database.Fetch<DictionaryDto>("WHERE pk > 0");
                foreach (var dto in dtos)
                {
                    yield return Get(dto.PrimaryKey);
                }
            }
        }

        protected override IEnumerable<IDictionaryItem> PerformGetByQuery(IQuery<IDictionaryItem> query)
        {
            var sqlClause = GetBaseQuery(false);
            var translator = new SqlTranslator<IDictionaryItem>(sqlClause, query);
            var sql = translator.Translate();

            var dtos = Database.Fetch<DictionaryDto, LanguageTextDto>(sql);

            foreach (var dto in dtos)
            {
                yield return Get(dto.PrimaryKey);
            }
        }

        #endregion

        #region Overrides of PetaPocoRepositoryBase<int,DictionaryItem>

        protected override Sql GetBaseQuery(bool isCount)
        {
            var sql = new Sql();
            sql.Select(isCount ? "COUNT(*)" : "*");
            sql.From("cmsDictionary");
            sql.InnerJoin("cmsLanguageText ON ([cmsDictionary].[id] = [cmsLanguageText].[UniqueId])");
            return sql;
        }

        protected override string GetBaseWhereClause()
        {
            return "[cmsDictionary].[id] = @Id";
        }

        protected override IEnumerable<string> GetDeleteClauses()
        {
            return new List<string>();
        }

        /// <summary>
        /// Returns the Top Level Parent Guid Id
        /// </summary>
        protected override Guid NodeObjectTypeId
        {
            get { return new Guid("41c7638d-f529-4bff-853e-59a0c2fb1bde"); }
        }

        #endregion

        #region Unit of Work Implementation

        protected override void PersistNewItem(IDictionaryItem entity)
        {
            ((Entity)entity).AddingEntity();

            var factory = new DictionaryItemFactory();
            var dto = factory.BuildDto(entity);

            var id = Convert.ToInt32(Database.Insert(dto));
            entity.Id = id;

            var translationFactory = new DictionaryTranslationFactory(entity.Key, null);
            foreach (var translation in entity.Translations)
            {
                var textDto = translationFactory.BuildDto(translation);
                translation.Id = Convert.ToInt32(Database.Insert(textDto));
            }

            ((ICanBeDirty)entity).ResetDirtyProperties();
        }

        protected override void PersistUpdatedItem(IDictionaryItem entity)
        {
            ((Entity)entity).UpdatingEntity();

            var factory = new DictionaryItemFactory();
            var dto = factory.BuildDto(entity);

            Database.Update(dto);

            var translationFactory = new DictionaryTranslationFactory(entity.Key, null);
            foreach (var translation in entity.Translations)
            {
                var textDto = translationFactory.BuildDto(translation);
                if(translation.HasIdentity)
                {
                    Database.Update(textDto);
                }
                else
                {
                    translation.Id = Convert.ToInt32(Database.Insert(dto));
                }
            }

            ((ICanBeDirty)entity).ResetDirtyProperties();
        }

        protected override void PersistDeletedItem(IDictionaryItem entity)
        {
            RecursiveDelete(entity.Key);

            Database.Delete<LanguageTextDto>("WHERE UniqueId = @Id", new { Id = entity.Key});

            Database.Delete<DictionaryDto>("WHERE id = @Id", new { Id = entity.Key });
        }

        private void RecursiveDelete(Guid parentId)
        {
            var list = Database.Fetch<DictionaryDto>("WHERE parent = @ParentId", new {ParentId = parentId});
            foreach (var dto in list)
            {
                RecursiveDelete(dto.Id);

                Database.Delete<LanguageTextDto>("WHERE UniqueId = @Id", new { Id = dto.Id });
                Database.Delete<DictionaryDto>("WHERE id = @Id", new { Id = dto.Id });
            }
        }

        #endregion
    }
}