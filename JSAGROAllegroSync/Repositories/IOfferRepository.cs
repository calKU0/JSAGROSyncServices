using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
{
    public interface IOfferRepository
    {
        Task GetAllOffers();

        Task<bool> UpsertOffer(int offerId);
    }
}