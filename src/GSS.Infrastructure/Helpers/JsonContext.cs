using System.Text.Json.Serialization;
using GSS.Core.Models;

namespace GSS.Infrastructure.Helpers;

/// <summary>
/// Source-generated JSON context for external service models.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(SsoAuthResponse))]
[JsonSerializable(typeof(DepartmentModel))]
[JsonSerializable(typeof(RoleTypeModel))]
[JsonSerializable(typeof(RoleModel))]
[JsonSerializable(typeof(PositionModel))]
[JsonSerializable(typeof(PersonPositionModel))]
[JsonSerializable(typeof(AsmResponseModel))]
[JsonSerializable(typeof(ApplicationAccessModel))]
[JsonSerializable(typeof(ExternalApiResponse<AsmResponseModel>))]
[JsonSerializable(typeof(ApiErrorModel))]
[JsonSerializable(typeof(RenewTokenRequest))]
[JsonSerializable(typeof(ExternalApiResponse<DepartmentModel>))]
[JsonSerializable(typeof(ExternalApiResponse<RoleTypeModel>))]
[JsonSerializable(typeof(ExternalApiResponse<RoleModel>))]
[JsonSerializable(typeof(ExternalApiResponse<PositionModel>))]
[JsonSerializable(typeof(ExternalApiResponse<PersonPositionModel>))]
public partial class JsonContext : JsonSerializerContext
{
}
