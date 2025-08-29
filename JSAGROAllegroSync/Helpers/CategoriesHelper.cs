using JSAGROAllegroSync.Models.Product;
using System.Collections.Generic;

namespace JSAGROAllegroSync.Helpers
{
    public static class CategoriesHelper
    {
        public static (string Path, List<int> PathIds) GetCategoryFullPathWithIds(ProductCategory cat, Dictionary<int, ProductCategory> allCategories)
        {
            var path = new List<string>();
            var ids = new List<int>();
            var current = cat;

            while (current != null)
            {
                path.Insert(0, current.Name);
                ids.Insert(0, current.CategoryId);

                current = allCategories.TryGetValue(current.ParentID, out var parent) ? parent : null;
            }

            return (string.Join("/", path), ids);
        }

        public static string GetCategoryFullPath(Application app, Dictionary<int, Application> allApplications)
        {
            var path = new List<string>();
            var current = app;
            while (current != null)
            {
                path.Insert(0, current.Name);
                current = allApplications.ContainsKey(current.ParentID)
                    ? allApplications[current.ParentID]
                    : null;
            }
            return string.Join("/", path);
        }
    }
}