using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Messaging;

/// <summary>Traite une commande sans valeur de retour.</summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Traite une commande qui renvoie une valeur.</summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}