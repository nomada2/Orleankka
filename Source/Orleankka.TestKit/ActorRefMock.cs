using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using Orleans;

namespace Orleankka.TestKit
{
    using Core;

    public class ActorRefMock : ActorRef
    {
        static ActorRefMock()
        {
            OrleansSerialization.Hack();
        }

        readonly IMessageSerializer serializer;
        readonly List<IExpectation> expectations = new List<IExpectation>();

        public readonly List<RecordedMessage> Received = new List<RecordedMessage>();

        public ActorRefMock(ActorPath path, IMessageSerializer serializer = null)
            : base(path)
        {
            this.serializer = serializer;
        }

        public CommandExpectation<TCommand> ExpectTell<TCommand>(Expression<Func<TCommand, bool>> match = null)
        {
            var expectation = new CommandExpectation<TCommand>(match ?? (_ => true));
            expectations.Add(expectation);
            return expectation;
        }

        public QueryExpectation<TQuery> ExpectAsk<TQuery>(Expression<Func<TQuery, bool>> match = null)
        {
            var expectation = new QueryExpectation<TQuery>(match ?? (_ => true));
            expectations.Add(expectation);
            return expectation;
        }

        public override Task Tell(object message)
        {
            message = Reserialize(message);

            var expectation = Match(message);
            var expected = expectation != null;

            Received.Add(new RecordedMessage(expected, message, typeof(DoNotExpectResult)));

            if (expected)
                expectation.Apply();

            return TaskDone.Done;
        }

        public override Task<TResult> Ask<TResult>(object message)
        {
            message = Reserialize(message);

            var expectation = Match(message);
            var expected = expectation != null;

            Received.Add(new RecordedMessage(expected, message, typeof(TResult)));

            return expected 
                       ? Task.FromResult((TResult) expectation.Apply()) 
                       : Task.FromResult(default(TResult));
        }

        IExpectation Match(object message) => expectations.FirstOrDefault(x => x.Match(message));
        object Reserialize(object message) => OrleansSerialization.Reserialize(serializer, message);
    }

    public class RecordedMessage
    {
        public readonly bool Expected;
        public readonly object Message;
        public readonly Type Result;

        public RecordedMessage(bool expected, object message, Type result)
        {
            Expected = expected;
            Message = message;
            Result = result;
        }
    }

    public class DoNotExpectResult
    {}
}