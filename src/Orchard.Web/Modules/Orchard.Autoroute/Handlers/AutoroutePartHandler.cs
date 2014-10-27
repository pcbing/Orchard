﻿using System;
using System.Linq;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Handlers;
using Orchard.Autoroute.Models;
using Orchard.Data;
using Orchard.Autoroute.Services;
using Orchard.Localization;
using Orchard.UI.Notify;

namespace Orchard.Autoroute.Handlers {
    public class AutoroutePartHandler : ContentHandler {

        private readonly Lazy<IAutorouteService> _autorouteService;
        private readonly IOrchardServices _orchardServices;

        public Localizer T { get; set; }

        public AutoroutePartHandler(
            IRepository<AutoroutePartRecord> autoroutePartRepository,
            Lazy<IAutorouteService> autorouteService,
            IOrchardServices orchardServices) {

            Filters.Add(StorageFilter.For(autoroutePartRepository));
            _autorouteService = autorouteService;
            _orchardServices = orchardServices;

            OnUpdated<AutoroutePart>((ctx, part) => CreateAlias(part));

            OnCreated<AutoroutePart>((ctx, part) => {
                // non-draftable items
                if (part.ContentItem.VersionRecord == null) {
                    PublishAlias(part);
                }
            });

            // OnVersioned<AutoroutePart>((ctx, part1, part2) => CreateAlias(part1));

            OnPublished<AutoroutePart>((ctx, part) => PublishAlias(part));

            // Remove alias if removed or unpublished
            OnRemoved<AutoroutePart>((ctx, part) => RemoveAlias(part));
            OnUnpublished<AutoroutePart>((ctx, part) => RemoveAlias(part));

            // Register alias as identity
            OnGetContentItemMetadata<AutoroutePart>((ctx, part) => {
                if (part.DisplayAlias != null)
                    ctx.Metadata.Identity.Add("alias", part.DisplayAlias);
            });
        }

        protected override void Imported(ImportContentContext context) {
            var importedItem = context.ContentItem.As<AutoroutePart>();
            if (importedItem != null && importedItem.DisplayAlias == "/") {
                PublishAlias(importedItem);
            }
        }

        private void CreateAlias(AutoroutePart part) {
            ProcessAlias(part);
        }

        private void PublishAlias(AutoroutePart part) {
            if (part.Processed) return;
            ProcessAlias(part);

            // should it become the home page ?
            if (part.DisplayAlias == "/") {
                part.DisplayAlias = String.Empty;

                // regenerate the alias for the previous home page
                var currentHomePages = _orchardServices.ContentManager.Query<AutoroutePart, AutoroutePartRecord>().Where(x => x.DisplayAlias == "").List();
                foreach (var current in currentHomePages.Where(x => x.Id != part.Id)) {
                    if (current != null) {
                        current.CustomPattern = String.Empty; // force the regeneration
                        current.DisplayAlias = _autorouteService.Value.GenerateAlias(current);
                    }
                    _autorouteService.Value.PublishAlias(current);
                }
            }

            _autorouteService.Value.PublishAlias(part);
            part.Processed = true;
        }

        private void ProcessAlias(AutoroutePart part) {
            // generate an alias if one as not already been entered
            if (String.IsNullOrWhiteSpace(part.DisplayAlias)) {
                part.DisplayAlias = _autorouteService.Value.GenerateAlias(part);
            }

            // if the generated alias is empty, compute a new one 
            if (String.IsNullOrWhiteSpace(part.DisplayAlias)) {
                _autorouteService.Value.ProcessPath(part);
                _orchardServices.Notifier.Warning(T("The permalink could not be generated, a new slug has been defined: \"{0}\"", part.Path));
                return;
            }

            // should it become the home page ?
            if (part.DisplayAlias != "/" && _orchardServices.Authorizer.Authorize(Permissions.SetHomePage)) {
                // if it's the current home page, do nothing
                var currentHomePages = _orchardServices.ContentManager.Query<AutoroutePart, AutoroutePartRecord>().Where(x => x.DisplayAlias == "").List();
                if (currentHomePages.Any(x => x.Id == part.Id)) {
                    return;
                }

                var previous = part.Path;
                if (!_autorouteService.Value.ProcessPath(part))
                    _orchardServices.Notifier.Warning(T("Permalinks in conflict. \"{0}\" is already set for a previously created {2} so now it has the slug \"{1}\"",
                                                 previous, part.Path, part.ContentItem.ContentType));
            }
        }

        void RemoveAlias(AutoroutePart part) {
            _autorouteService.Value.RemoveAliases(part);
        }

        protected override void RegisteringIdentityResolvers(RegisteringIdentityResolversContext context) {
            context.Register(
                contentIdentity => contentIdentity.Has("alias"),
                contentIdentity => {
                    var identifier = contentIdentity.Get("alias");

                    if (identifier == null) {
                        return null;
                    }

                    return _orchardServices.ContentManager
                        .Query<AutoroutePart, AutoroutePartRecord>()
                        .Where(p => p.DisplayAlias == identifier)
                        .List<ContentItem>()
                        .FirstOrDefault();
                });
        }
    }
}
