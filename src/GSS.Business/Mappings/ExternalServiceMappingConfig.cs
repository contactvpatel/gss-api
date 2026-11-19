using GSS.Business.DTOs;
using GSS.Core.Models;
using Mapster;

namespace GSS.Business.Mappings;

/// <summary>
/// Mapster maps for SSO, MIS and ASM models (Core) to their Business DTOs.
/// </summary>
public class ExternalServiceMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Core.Models.SsoAuthResponse, DTOs.SsoAuthResponse>();
        config.NewConfig<DepartmentModel, DepartmentDto>();
        config.NewConfig<RoleTypeModel, RoleTypeDto>();
        config.NewConfig<RoleModel, RoleDto>();
        config.NewConfig<PositionModel, PositionDto>();
        config.NewConfig<PersonPositionModel, PersonPositionDto>();
        // System.Text.Json ignores nullable annotations, so ASM's "applicationAccess": null (or a null entry) reaches the
        // model; the cached DTO always holds a list of entries, or the permission check would throw (500) until exp.
        config.NewConfig<AsmResponseModel, AsmResponseDto>()
            .Map(dest => dest.ApplicationAccess, src => (src.ApplicationAccess ?? new List<ApplicationAccessModel>()).Where(a => a != null).ToList());
        config.NewConfig<ApplicationAccessModel, ApplicationAccessDto>();
    }
}
