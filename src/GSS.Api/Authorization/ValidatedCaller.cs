namespace GSS.Api.Authorization;

/// <summary>The caller's position selected with the x-app-* headers, confirmed against their MIS positions.</summary>
/// <param name="UserId">The authenticated user's id (the <c>pid</c> claim).</param>
/// <param name="PositionId">Position from <c>x-app-position-id</c>.</param>
/// <param name="RoleId">Role from <c>x-app-role-id</c>.</param>
/// <param name="DivisionId">Division from <c>x-app-division-id</c>.</param>
/// <param name="DepartmentIds">Departments from <c>x-app-department-id</c>.</param>
public sealed record ValidatedCaller(string UserId, int PositionId, int RoleId, int DivisionId, IReadOnlyList<int> DepartmentIds);
