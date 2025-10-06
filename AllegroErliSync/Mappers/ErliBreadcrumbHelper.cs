using AllegroErliSync.DTOs;
using AllegroErliSync.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Mappers
{
    public static class ErliBreadcrumbHelper
    {
        public static List<ErliCategoryBreadcrumb> BuildBreadcrumb(int categoryId, Dictionary<int, AllegroCategory> categories)
        {
            var breadcrumb = new List<ErliCategoryBreadcrumb>();
            var current = categories.Values.FirstOrDefault(c => c.CategoryId == categoryId.ToString());

            while (current != null)
            {
                breadcrumb.Insert(0, new ErliCategoryBreadcrumb
                {
                    Id = current.CategoryId.ToString(),
                    Name = current.Name
                });

                if (current.ParentId == null) break;
                current = categories.Values.FirstOrDefault(c => c.Id == current.ParentId);
            }

            return breadcrumb;
        }
    }
}