using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        public Task GetAllOffers()
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpsertOffer(int offerId)
        {
            throw new NotImplementedException();
        }
    }
}