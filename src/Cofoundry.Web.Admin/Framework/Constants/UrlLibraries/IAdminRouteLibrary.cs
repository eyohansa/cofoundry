﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Cofoundry.Web.Admin
{
    public interface IAdminRouteLibrary
    {
        AccountRouteLibrary Account { get; }
        AuthRouteLibrary Auth { get; }
        CustomEntitiesRouteLibrary CustomEntities { get; }
        DashboardRouteLibrary Dashboard { get; }
        DirectoriesRouteLibrary Directories { get; }
        DocumentsModuleRouteLibrary Documents { get; }
        ImagesModuleRouteLibrary Images { get; }
        PagesModuleRouteLibrary Pages { get; }
        PageTemplatesRouteLibrary PageTemplates { get; }
        RolesRouteLibrary Roles { get; }
        SettingsRouteLibrary Settings { get; }
        SetupRouteLibrary Setup { get; }
        SharedRouteLibrary Shared { get; }

        /// <summary>
        /// Route library for the alternative module (client implementation)
        /// module path.
        /// </summary>
        SharedAlternateRouteLibrary SharedAlternate { get; }
        UsersRouteLibrary Users { get; }
        VisualEditorRouteLibrary VisualEditor { get; }
    }
}