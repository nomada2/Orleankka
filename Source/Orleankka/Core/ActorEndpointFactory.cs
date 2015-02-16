﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Orleankka.Core
{
    using Codegen;

    static class ActorEndpointFactory
    {
        readonly static Dictionary<Type, ActorEndpointInvoker> invokers =
                    new Dictionary<Type, ActorEndpointInvoker>();

        public static ActorEndpointInvoker Invoker(Type type)
        {
            return invokers[type];
        }

        public static void Register(Type type)
        {
            var declaration = ActorEndpointDeclaration.From(type);
            invokers.Add(type, Bind(declaration.ToString()));
        }

        static ActorEndpointInvoker Bind(string name)
        {
            #if PACKAGE
                const string assemblyName = "Orleankka";
            #else
                const string assemblyName = "Orleankka.Core";
            #endif

            var factory = Type.GetType(
                "Orleankka.Core.Hardcore." 
                + name + ".ActorEndpointFactory, " 
                + assemblyName);

            return new ActorEndpointInvoker(factory);
        }

        public static void Reset()
        {
            invokers.Clear();
        }
    }

    class ActorEndpointInvoker
    {
        public readonly Func<string, object> GetProxy;
        public readonly Func<object, RequestEnvelope, Task<ResponseEnvelope>> Receive;

        internal ActorEndpointInvoker(Type factory)
        {
            GetProxy = BindGetProxy(factory);
            Receive = BindReceive(factory);
        }

        static Func<string, object> BindGetProxy(Type factory)
        {
            var parameter = Expression.Parameter(typeof(string), "primaryKey");
            var call = Expression.Call(GetGrainMethod(factory), new Expression[] {parameter});
            return Expression.Lambda<Func<string, object>>(call, parameter).Compile();
        }

        static Func<object, RequestEnvelope, Task<ResponseEnvelope>> BindReceive(Type factory)
        {
            var @interface = GetGrainMethod(factory).ReturnType;

            ParameterExpression target = Expression.Parameter(typeof(object));
            ParameterExpression request = Expression.Parameter(typeof(RequestEnvelope));

            var conversion = Expression.Convert(target, @interface);
            var call = Expression.Call(conversion, GetReceiveMethod(@interface), new Expression[] {request});

            return Expression.Lambda<Func<object, RequestEnvelope, Task<ResponseEnvelope>>>(call, target, request).Compile();
        }

        static MethodInfo GetGrainMethod(Type factory)
        {
            return factory.GetMethod("GetGrain", 
                    BindingFlags.Public | BindingFlags.Static, 
                    null, new[] {typeof(string)}, null);
        }

        static MethodInfo GetReceiveMethod(Type @interface)
        {
            return @interface.GetMethod("Receive");
        }
    }
}