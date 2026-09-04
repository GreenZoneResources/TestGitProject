using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.RoleManagement;
using BulkReversal.Application.Features.RoleManagement.Dtos;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BulkReversal.UnitTests.Upload;

public class RoleAssignmentServiceTests
{
    private readonly Mock<IUserRoleAssignmentRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IAuditService> _auditService = new();

    public RoleAssignmentServiceTests()
    {
        _currentUser.Setup(u => u.UserId).Returns("admin1");
        _currentUser.Setup(u => u.UserName).Returns("Root Admin");
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private RoleAssignmentService CreateSut() => new(
        _repository.Object, _unitOfWork.Object, _currentUser.Object, _auditService.Object, NullLogger<RoleAssignmentService>.Instance);

    [Fact]
    public async Task AssignRoleAsync_NoExistingActiveAssignment_CreatesAndSaves()
    {
        _repository.Setup(r => r.HasActiveAssignmentAsync("u1", AppRoles.SettlementApprover, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut();
        var result = await sut.AssignRoleAsync(new AssignRoleRequest("u1", "User One", "u1@bank.local", AppRoles.SettlementApprover));

        Assert.Equal("u1", result.UserId);
        Assert.Equal(AppRoles.SettlementApprover, result.Role);
        Assert.True(result.IsActive);
        _repository.Verify(r => r.Add(It.IsAny<UserRoleAssignment>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_AlreadyActivelyAssigned_ThrowsConflictAndDoesNotSave()
    {
        _repository.Setup(r => r.HasActiveAssignmentAsync("u1", AppRoles.SettlementApprover, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = CreateSut();

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            sut.AssignRoleAsync(new AssignRoleRequest("u1", "User One", "u1@bank.local", AppRoles.SettlementApprover)));

        _repository.Verify(r => r.Add(It.IsAny<UserRoleAssignment>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeRoleAsync_ActiveAssignment_MarksInactiveAndSaves()
    {
        var assignment = UserRoleAssignment.Create("u1", "User One", "u1@bank.local", AppRoles.SettlementApprover, "admin0", "Prior Admin");
        _repository.Setup(r => r.GetByIdAsync(assignment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        var sut = CreateSut();
        await sut.RevokeRoleAsync(assignment.Id);

        Assert.False(assignment.IsActive);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeRoleAsync_UnknownId_ThrowsNotFoundAppException()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserRoleAssignment?)null);

        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundAppException>(() => sut.RevokeRoleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RevokeRoleAsync_AlreadyRevoked_ThrowsDomainException()
    {
        var assignment = UserRoleAssignment.Create("u1", "User One", "u1@bank.local", AppRoles.SettlementApprover, "admin0", "Prior Admin");
        assignment.Revoke("admin0", "Prior Admin");
        _repository.Setup(r => r.GetByIdAsync(assignment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        var sut = CreateSut();

        await Assert.ThrowsAsync<DomainException>(() => sut.RevokeRoleAsync(assignment.Id));
    }
}
