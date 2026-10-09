using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal sealed class RequestMiddlewareHandlerCall : MethodCall
{
    private readonly MethodCall handlerCall;
    private readonly BeforeRequestMiddlewareFrame? before;
    private readonly AfterRequestMiddlewareFrame? after;
    private readonly FinallyRequestMiddlewareFrame? finallyFrame;

    internal RequestMiddlewareHandlerCall(
        Type requestType,
        MethodCall handlerCall,
        RequestMiddlewareRegistry registry) : base(handlerCall.HandlerType, handlerCall.Method)
    {
        this.handlerCall = handlerCall;
        IsAsync = true;
        Target = handlerCall.Target;
        IsLocal = handlerCall.IsLocal;
        ActivityEventBeforeCall = handlerCall.ActivityEventBeforeCall;
        ActivityEventAfterCall = handlerCall.ActivityEventAfterCall;
        Array.Copy(handlerCall.Arguments, Arguments, Arguments.Length);
        foreach (var alias in handlerCall.Aliases)
        {
            Aliases.Add(alias.Key, alias.Value);
        }

        before = BeforeRequestMiddlewareFrame.TryCreate(
            requestType, ReturnVariable!, HandlerType, registry.BeforeMiddlewareTypes);
        after = AfterRequestMiddlewareFrame.TryCreate(
            requestType, ReturnVariable!, registry.AfterMiddlewareTypes);
        finallyFrame = FinallyRequestMiddlewareFrame.TryCreate(
            requestType, ReturnVariable!, registry.FinallyMiddlewareTypes);
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        return base.FindVariables(chain)
            .Concat(before?.FindVariables(chain) ?? [])
            .Concat(after?.FindVariables(chain) ?? [])
            .Concat(finallyFrame?.FindVariables(chain) ?? []);
    }

    public override bool CanReturnTask()
    {
        return false;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.WriteLine($"{RequestMiddlewareCodeGeneration.GetCodeTypeName(ReturnVariable!.VariableType)} {ReturnVariable.Usage} = default!;");
        if (finallyFrame is null)
        {
            GenerateBody(method, writer);
        }
        else
        {
            finallyFrame.Wrap(writer, () => GenerateBody(method, writer));
        }

        Next?.GenerateCode(method, writer);
    }

    private void GenerateBody(GeneratedMethod method, ISourceWriter writer)
    {
        before?.GenerateCode(method, writer);

        handlerCall.Target = Target;
        handlerCall.IsLocal = IsLocal;
        handlerCall.ActivityEventBeforeCall = ActivityEventBeforeCall;
        handlerCall.ActivityEventAfterCall = ActivityEventAfterCall;
        Array.Copy(Arguments, handlerCall.Arguments, Arguments.Length);
        writer.Write($"BLOCK:if ({ReturnVariable!.Usage} is null)");
        handlerCall.AssignResultTo(ReturnVariable);
        handlerCall.GenerateCode(method, writer);
        writer.FinishBlock();

        after?.GenerateCode(method, writer);
    }
}
