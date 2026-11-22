using System;
using System.Linq.Expressions;
using System.Reflection;

// Bind the SDK's real ref/out event type once; do not duplicate its native ABI.
internal sealed class SdkControllerEventReader
{
    internal struct Result {internal int ReturnCode,Type,Code,Value;internal object Raw;}
    readonly Func<int,bool,Result> read;
    internal readonly string Mode;
    SdkControllerEventReader(Func<int,bool,Result> read,string mode){this.read=read;Mode=mode;}
    internal Result Read(int slot,bool capture)=>read(slot,capture);

    internal static SdkControllerEventReader Bind(MethodInfo method,bool compile=true) {
        var args=method?.GetParameters();
        if(method==null || !method.IsStatic || method.ReturnType!=typeof(int) || args.Length!=2 ||
            args[0].ParameterType!=typeof(int) || !args[1].ParameterType.IsByRef)
            throw new NotSupportedException("Controller SDK read signature differs");
        var eventType=args[1].ParameterType.GetElementType();
        const BindingFlags fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var type=eventType.GetField("type",fields);var code=eventType.GetField("code",fields);var value=eventType.GetField("value",fields);
        if(!eventType.IsValueType || type?.FieldType!=typeof(int) || code?.FieldType!=typeof(int) || value?.FieldType!=typeof(int))
            throw new NotSupportedException("Controller SDK event fields differ");
        string fallback="disabled";
        if(compile) {
            try {
                var slot=Expression.Parameter(typeof(int),"slot");
                var capture=Expression.Parameter(typeof(bool),"capture");
                var native=Expression.Variable(eventType,"native");
                var rc=Expression.Variable(typeof(int),"rc");
                var result=Expression.MemberInit(Expression.New(typeof(Result)),
                    Expression.Bind(typeof(Result).GetField("ReturnCode",fields),rc),
                    Expression.Bind(typeof(Result).GetField("Type",fields),Expression.Field(native,type)),
                    Expression.Bind(typeof(Result).GetField("Code",fields),Expression.Field(native,code)),
                    Expression.Bind(typeof(Result).GetField("Value",fields),Expression.Field(native,value)),
                    Expression.Bind(typeof(Result).GetField("Raw",fields),Expression.Condition(capture,
                        Expression.Convert(native,typeof(object)),Expression.Constant(null,typeof(object)))));
                // A ref SDK may leave the event untouched on an empty read.
                // Start every call with a fresh zero value to prevent stale taps.
                var body=Expression.Block(new[]{native,rc},Expression.Assign(native,Expression.Default(eventType)),
                    Expression.Assign(rc,Expression.Call(method,slot,native)),result);
                return new SdkControllerEventReader(Expression.Lambda<Func<int,bool,Result>>(body,slot,capture).Compile(),"compiled");
            } catch(Exception e) {fallback=e.GetType().Name;}
        }
        // Older runtimes may prohibit compiled access to the private SDK type.
        // Keep the established reflection path available, including diagnostics.
        var call=new object[2];int previousSlot=int.MinValue;
        return new SdkControllerEventReader((slot,capture)=> {
            if(slot!=previousSlot){call[0]=slot;previousSlot=slot;}
            call[1]=Activator.CreateInstance(eventType);
            int rc=(int)method.Invoke(null,call);
            return new Result{ReturnCode=rc,Type=(int)type.GetValue(call[1]),Code=(int)code.GetValue(call[1]),
                Value=(int)value.GetValue(call[1]),Raw=capture?call[1]:null};
        },"reflection_"+fallback);
    }
}
