using JSAGROSyncServices.Contracts.Data.Enums;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

// Wspolne dla serwisow produktowych i Erli - konto przekazuje host przy rejestracji.
namespace JSAGROSyncServices.Infrastructure.Repositories
{
    public class AllegroDeliveryMethodRepository : IAllegroDeliveryMethodRepository
    {
        private readonly AllegroAccount _account;
        private readonly DapperContext _context;

        public AllegroDeliveryMethodRepository(DapperContext context, AllegroAccount account)
        {
            _account = account;
            _context = context;
        }

        public async Task UpsertAllegroDeliveryMethods(IEnumerable<AllegroDeliveryMethod> deliveryMethods, CancellationToken ct = default)
        {
            if (deliveryMethods == null || !deliveryMethods.Any())
            {
                return;
            }

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var deliveryMethod in deliveryMethods)
                {
                    var deliveryMethodId = await connection.ExecuteScalarAsync<int>(
                        "AllegroDeliveryMethods_Upsert",
                        new
                        {
                            deliveryMethod.AllegroId,
                            Account = (int)deliveryMethod.Account,
                            deliveryMethod.Name,
                            deliveryMethod.ManagedByAllegro,
                            deliveryMethod.IsFulfillment
                        },
                        transaction,
                        commandType: CommandType.StoredProcedure);

                    await connection.ExecuteAsync(
                        "AllegroDeliveryMethodDetails_DeleteByDeliveryMethodId",
                        new { AllegroDeliveryMethodId = deliveryMethodId },
                        transaction,
                        commandType: CommandType.StoredProcedure);

                    if (deliveryMethod.AllegroDeliveryMethodDetails == null || !deliveryMethod.AllegroDeliveryMethodDetails.Any())
                    {
                        continue;
                    }

                    await connection.ExecuteAsync(
                        "AllegroDeliveryMethodDetails_Upsert",
                        deliveryMethod.AllegroDeliveryMethodDetails.Select(detail => new
                        {
                            AllegroDeliveryMethodId = deliveryMethodId,
                            detail.Name,
                            detail.PaymentPolicy,
                            detail.MaxPackageQuantity,
                            detail.MaxPackageWeight,
                            detail.MaxPackageWeightUnit,
                            detail.FirstItemAmount,
                            detail.FirstItemCurrency,
                            detail.NextItemAmount,
                            detail.NextItemCurrency,
                            detail.ShippingTimeFrom,
                            detail.ShippingTimeTo
                        }),
                        transaction,
                        commandType: CommandType.StoredProcedure);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IEnumerable<AllegroDeliveryMethod>> GetAllegroDeliveryMethods(CancellationToken ct = default)
        {
            using var connection = _context.CreateConnection();

            using var grid = await connection.QueryMultipleAsync(
                "AllegroDeliveryMethods_GetAll",
                new { Account = (int)_account },
                commandType: CommandType.StoredProcedure);

            var methods = grid.Read<AllegroDeliveryMethod>().ToList();
            var details = grid.Read<AllegroDeliveryMethodDetails>().ToList();
            var detailsLookup = details.ToLookup(x => x.AllegroDeliveryMethodId);

            foreach (var method in methods)
            {
                method.AllegroDeliveryMethodDetails = detailsLookup[method.Id].ToList();
            }

            return methods;
        }
    }
}
