using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Commands.SavedQueries;

public sealed record ToggleFavoriteCommand(Guid Id) : IRequest<bool>;

public sealed class ToggleFavoriteCommandHandler(ISavedQueryRepository repository)
    : IRequestHandler<ToggleFavoriteCommand, bool>
{
    public async Task<bool> Handle(ToggleFavoriteCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.Id);

        existing.IsFavorite = !existing.IsFavorite;
        repository.Update(existing);
        await repository.SaveChangesAsync(cancellationToken);
        return existing.IsFavorite;
    }
}
