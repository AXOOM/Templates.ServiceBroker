using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenServiceBroker.Catalogs;
using OpenServiceBroker.Errors;
using OpenServiceBroker.Instances;

namespace MyVendor.ServiceBroker.Broker
{
    public class ServiceInstanceService : IServiceInstanceBlocking
    {
        private readonly DbContext _context;
        private readonly IServiceInstanceMetrics _metrics;
        private readonly ILogger<ServiceInstanceService> _logger;
        private readonly BrokerOptions _options;

        public ServiceInstanceService(DbContext context, IOptions<BrokerOptions> options, IServiceInstanceMetrics metrics, ILogger<ServiceInstanceService> logger)
        {
            _context = context;
            _metrics = metrics;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<ServiceInstanceResource> FetchAsync(string instanceId)
        {
            _logger.LogTrace("Read instance {0}", instanceId);

            var entity = await _context.ServiceInstances.FindAsync(instanceId);
            if (entity == null) throw new NotFoundException($"Instance '{instanceId}' not found.");

            return new ServiceInstanceResource
            {
                ServiceId = entity.ServiceId,
                PlanId = entity.PlanId,
                Parameters = JsonConvert.DeserializeObject<JObject>(entity.Parameters)
            };
        }

        public async Task<ServiceInstanceProvision> ProvisionAsync(ServiceInstanceContext context, ServiceInstanceProvisionRequest request)
        {
            _logger.LogInformation("Provisioning instance {0} as service {1}.", context.InstanceId, request.ServiceId);

            if (GetService(request.ServiceId).Plans.All(x => x.Id != request.PlanId))
                throw new BadRequestException($"Unknown plan ID '{request.PlanId}'.");
            
            var entity = await _context.ServiceInstances.FindAsync(context.InstanceId);
            if (entity != null)
            {
                if (entity.ServiceId == request.ServiceId
                 && entity.PlanId == request.PlanId
                 && JsonConvert.SerializeObject(request.Parameters) == (entity.Parameters ?? "null"))
                    return new ServiceInstanceProvision {Unchanged = true};
                else
                    throw new ConflictException($"There is already an instance {context.InstanceId} with different settings.");
            }

            await _context.ServiceInstances.AddAsync(new ServiceInstanceEntity
            {
                Id = context.InstanceId,
                ServiceId = request.ServiceId,
                PlanId = request.PlanId,
                Parameters = JsonConvert.SerializeObject(request.Parameters)
            });
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new ConflictException(ex.InnerException?.Message ?? ex.Message);
            }

            _metrics.Provisioned(request.ServiceId);
            return new ServiceInstanceProvision();
        }

        [NotNull]
        private Service GetService([NotNull] string id)
        {
            var service = _options.Catalog.Services.FirstOrDefault(x => x.Id == id);
            if (service == null)
                throw new BadRequestException($"Unknown service ID '{id}'.");
            return service;
        }

        public async Task UpdateAsync(ServiceInstanceContext context, ServiceInstanceUpdateRequest request)
        {
            _logger.LogInformation("Updating instance {0} as service {1}.", context.InstanceId, request.ServiceId);

            var entity = await _context.ServiceInstances.FindAsync(context.InstanceId);
            if (entity == null) throw new NotFoundException($"Instance '{context.InstanceId}' not found.");

            if (request.ServiceId != entity.ServiceId)
                throw new BadRequestException($"Cannot change service ID of instance '{context.InstanceId}' from '{entity.ServiceId}' to '{request.ServiceId}'.");
            if (request.PlanId != null && request.PlanId != entity.PlanId)
            {
                var service = GetService(request.ServiceId);
                if (!service.PlanUpdateable)
                    throw new BadRequestException($"Service ID '{request.ServiceId}' does not allow changing the Plan ID.");
                if (service.Plans.All(x => x.Id != request.PlanId))
                    throw new BadRequestException($"Unknown plan ID '{request.PlanId}'.");
                entity.PlanId = request.PlanId;
            }
            if (request.Parameters != null)
                entity.Parameters = JsonConvert.SerializeObject(request.Parameters);

            _context.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeprovisionAsync(ServiceInstanceContext context, string serviceId, string planId)
        {
            _logger.LogInformation("Deprovisioning instance {0}.", context.InstanceId);

            var entity = await _context.ServiceInstances.FindAsync(context.InstanceId);
            if (entity == null)
                throw new GoneException($"Instance '{context.InstanceId}' not found.");
            if (entity.ServiceId != serviceId || entity.PlanId != planId)
                throw new BadRequestException($"Service and/or plan ID for instance '{context.InstanceId}' do not match.");

            _context.ServiceInstances.Remove(entity);
            await _context.SaveChangesAsync();

            _metrics.Deprovisioned();
        }
    }
}
