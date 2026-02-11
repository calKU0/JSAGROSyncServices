using Allegro.JSAGRO.Erli.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Shared.Data;
using System.Data;

namespace Allegro.JSAGRO.Erli.ProductsService.Repositories
{
    public class OfferRepository
    {
        private readonly DapperContext _context;

        public OfferRepository(DapperContext context)
        {
            _context = context;
        }

        public IEnumerable<AllegroOffer> GetOffersWithDetails()
        {
            using (var connection = _context.CreateConnection())
            {
                var storedProcedure = "dbo.AllegroOffers_GetWithDetails";

                var offerDict = new Dictionary<string, AllegroOffer>();

                connection.Query<AllegroOffer>(
                    storedProcedure,
                    new { Account = ServiceConstants.Account },
                    commandType: CommandType.StoredProcedure
                ).ToList().ForEach(offer =>
                {
                    if (!offerDict.ContainsKey(offer.Id))
                    {
                        offerDict.Add(offer.Id, offer);
                    }
                });

                return offerDict.Values;
            }
        }

        public void UpdateOffersExistsInErli(IEnumerable<AllegroOffer> offers)
        {
            using (var connection = _context.CreateConnection())
            {
                // Execute in a transaction for safety
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var offer in offers)
                    {
                        connection.Execute(
                            "dbo.AllegroOffers_UpdateExistsInErli",
                            new { ExistsInErli = offer.ExistsInErli, Id = offer.Id },
                            transaction,
                            commandType: CommandType.StoredProcedure
                        );
                    }
                    transaction.Commit();
                }
            }
        }

        public IEnumerable<AllegroOffer> GetOffersForErliCreation()
        {
            using (var connection = _context.CreateConnection())
            {
                var storedProcedure = "dbo.AllegroOffers_GetForErliCreation";

                var offerDict = new Dictionary<string, AllegroOffer>();

                connection.Query<AllegroOffer, AllegroOfferDescription, AllegroOfferAttribute, AllegroOffer>(
                    storedProcedure,
                    (offer, desc, attr) =>
                    {
                        if (!offerDict.TryGetValue(offer.Id, out var currentOffer))
                        {
                            currentOffer = offer;
                            currentOffer.Descriptions = new List<AllegroOfferDescription>();
                            currentOffer.Attributes = new List<AllegroOfferAttribute>();
                            offerDict.Add(currentOffer.Id, currentOffer);
                        }

                        if (desc != null && !currentOffer.Descriptions.Any(d => d.Id == desc.Id))
                            currentOffer.Descriptions.Add(desc);

                        if (attr != null && !currentOffer.Attributes.Any(a => a.Id == attr.Id))
                            currentOffer.Attributes.Add(attr);

                        return offer;
                    },
                    new { Account = ServiceConstants.Account },
                    splitOn: "Id,Id",
                    commandTimeout: 600,
                    commandType: CommandType.StoredProcedure
                ).ToList();

                return offerDict.Values;
            }
        }

        public IEnumerable<AllegroOffer> GetOffersForErliUpdate()
        {
            using (var connection = _context.CreateConnection())
            {
                var storedProcedure = "dbo.AllegroOffers_GetForErliUpdate";

                var offerDict = new Dictionary<string, AllegroOffer>();

                connection.Query<AllegroOffer, AllegroOfferDescription, AllegroOfferAttribute, AllegroOffer>(
                    storedProcedure,
                    (offer, desc, attr) =>
                    {
                        if (!offerDict.TryGetValue(offer.Id, out var currentOffer))
                        {
                            currentOffer = offer;
                            currentOffer.Descriptions = new List<AllegroOfferDescription>();
                            currentOffer.Attributes = new List<AllegroOfferAttribute>();
                            offerDict.Add(currentOffer.Id, currentOffer);
                        }

                        if (desc != null && !currentOffer.Descriptions.Any(d => d.Id == desc.Id))
                            currentOffer.Descriptions.Add(desc);

                        if (attr != null && !currentOffer.Attributes.Any(a => a.Id == attr.Id))
                            currentOffer.Attributes.Add(attr);

                        return offer;
                    },
                    new { Account = ServiceConstants.Account },
                    splitOn: "Id,Id",
                    commandTimeout: 600,
                    commandType: CommandType.StoredProcedure
                ).ToList();

                return offerDict.Values;
            }
        }
    }
}