using AllegroErliSync.Data;
using AllegroErliSync.Models;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Repositories
{
    public class OfferRepository
    {
        private readonly DapperContext _context;

        public OfferRepository(DapperContext context)
        {
            _context = context;
        }

        public IEnumerable<Offer> GetOffersWithDetails()
        {
            using (var connection = _context.CreateConnection())
            {
                var sql = @"
                SELECT Id, ExistsInErli
                FROM AllegroOffers";

                var offerDict = new Dictionary<string, Offer>();

                connection.Query<Offer>(sql).ToList().ForEach(offer =>
                {
                    if (!offerDict.ContainsKey(offer.Id))
                    {
                        offerDict.Add(offer.Id, offer);
                    }
                });

                return offerDict.Values;
            }
        }

        public void UpdateOffersExistsInErli(IEnumerable<Offer> offers)
        {
            using (var connection = _context.CreateConnection())
            {
                var sql = "UPDATE AllegroOffers SET ExistsInErli = @ExistsInErli WHERE Id = @Id";

                // Execute in a transaction for safety
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var offer in offers)
                    {
                        connection.Execute(sql, new { ExistsInErli = offer.ExistsInErli, Id = offer.Id }, transaction);
                    }
                    transaction.Commit();
                }
            }
        }

        public IEnumerable<Offer> GetOffersForErliCreation()
        {
            using (var connection = _context.CreateConnection())
            {
                // Step 1: get offers + descriptions
                var sqlOffers = @"
                    SELECT o.*, d.Id AS DescriptionId, d.Type AS DescType, d.Content
                    FROM AllegroOffers o
                    LEFT JOIN AllegroOfferDescriptions d ON o.Id = d.OfferId
                    WHERE ExistsInErli = 0";

                var offerDict = new Dictionary<string, Offer>();

                connection.Query<Offer, OfferDescription, Offer>(sqlOffers,
                    (offer, desc) =>
                    {
                        if (!offerDict.TryGetValue(offer.Id, out var currentOffer))
                        {
                            currentOffer = offer;
                            currentOffer.Descriptions = new List<OfferDescription>();
                            currentOffer.Attributes = new List<OfferAttribute>();
                            offerDict.Add(currentOffer.Id, currentOffer);
                        }

                        if (desc != null && !currentOffer.Descriptions.Any(d => d.DescriptionId == desc.DescriptionId))
                            currentOffer.Descriptions.Add(desc);

                        return offer;
                    },
                    splitOn: "DescriptionId"
                ).ToList();

                // Step 2: get all attributes separately
                var sqlAttrs = @"
                    SELECT *
                    FROM AllegroOfferAttributes
                    WHERE OfferId IN @OfferIds";

                var allAttrs = connection.Query<OfferAttribute>(sqlAttrs, new { OfferIds = offerDict.Keys.ToArray() });

                // Step 3: merge attributes
                foreach (var attr in allAttrs)
                {
                    if (offerDict.TryGetValue(attr.OfferId, out var offer))
                    {
                        offer.Attributes.Add(attr);
                    }
                }

                return offerDict.Values;
            }
        }

        public IEnumerable<Offer> GetOffersForErliUpdate()
        {
            using (var connection = _context.CreateConnection())
            {
                var sql = @"
                    SELECT o.*, d.Id AS DescriptionId, d.Type AS DescriptionType, d.Content,
                           a.Id AS AttributeId, a.AttributeId AS AttrId, a.Type AS AttrType, a.ValuesJson, a.ValuesIdsJson
                    FROM AllegroOffers o
                    LEFT JOIN AllegroOfferDescriptions d ON o.Id = d.OfferId
                    LEFT JOIN AllegroOfferAttributes a ON o.Id = a.OfferId
                    WHERE ExistsInErli = 1";

                var offerDict = new Dictionary<string, Offer>();

                connection.Query<Offer, OfferDescription, OfferAttribute, Offer>(sql, (offer, desc, attr) =>
                {
                    if (!offerDict.TryGetValue(offer.Id, out var currentOffer))
                    {
                        currentOffer = offer;
                        offerDict.Add(currentOffer.Id, currentOffer);
                    }

                    if (desc != null && !currentOffer.Descriptions.Any(d => d.DescriptionId == desc.DescriptionId))
                        currentOffer.Descriptions.Add(desc);

                    if (attr != null && !currentOffer.Attributes.Any(a => a.Id == attr.Id))
                        currentOffer.Attributes.Add(attr);

                    return currentOffer;
                },
                splitOn: "DescriptionId,AttributeId"
                ).ToList();

                return offerDict.Values;
            }
        }

        public IEnumerable<AllegroCategory> GetAllCategories()
        {
            using (var connection = _context.CreateConnection())
            {
                var sql = "SELECT Id, CategoryId, Name, ParentId FROM AllegroCategories";
                return connection.Query<AllegroCategory>(sql).ToList();
            }
        }
    }
}