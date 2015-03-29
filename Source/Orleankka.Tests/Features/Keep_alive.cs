﻿using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using NUnit.Framework;
using Orleans;

namespace Orleankka.Features
{
    namespace Keep_alive
    {
        using Meta;
        using Services;
        using Testing;

        [Serializable]
        public class SetReminder : Command
        {
            public TimeSpan Period;
        }

        [Serializable]
        public class HasBeenReminded : Query<bool>
        {}

        [Serializable]
        public class GetInstanceHashcode : Query<long>
        {}

        public abstract class TestActor : Actor
        {
            protected const int KeepAliveTimeoutInMinutes = 2;

            readonly IReminderService reminders;
            bool reminded;

            protected TestActor()
            {
                reminders = new ReminderService(this);
            }

            protected internal override void Define()
            {
                On((HasBeenReminded x)      => reminded);
                On((SetReminder x)          => reminders.Register("test", TimeSpan.Zero, x.Period));
                On((GetInstanceHashcode x)  => RuntimeHelpers.GetHashCode(this));
            }

            public override Task OnReminder(string id)
            {
                reminded = true;
                return TaskDone.Done;
            }
        }

        [TestFixture]
        [RequiresSilo(Fresh = true, DefaultKeepAliveTimeoutInMinutes = 1)]
        abstract class Tests<T> where T : Actor
        {
            IActorSystem system;

            [SetUp]
            public void SetUp()
            {
                system = TestActorSystem.Instance;
            }

            [Test]
            public async void When_just_activated()
            {
                var actor = system.FreshActorOf<T>();
                var hashcode = await actor.Ask(new GetInstanceHashcode());

                await Task.Delay(TimeSpan.FromMinutes(1.5));

                Assert.AreEqual(hashcode, await actor.Ask(new GetInstanceHashcode()),
                    "Should respect per-type keepalive timeout and not being GC'ed as per global keepalive timeout");
            }

            [Test]
            public async void When_external_request_arrives()
            {
                // global keepalive timeout is 1 minute,  per-type timeout is 2 minutes
                // we wait 1.5 minute, and then 0.5 minutes are still left from per-type specified timeout
                // then we're making another request, which should delay deactivation for another 2 minutes (per-type timeout)
                // if automatic keepalive prolongation doesn't work, an actor will still be alive for at least minute, as per global keepalive timeout
                // we wait again for 1.5 minutes to disprove previous assumption (that automatic prolongation is not working) 

                var actor = system.FreshActorOf<T>();

                var hashcode = await actor.Ask(new GetInstanceHashcode());
                await Task.Delay(TimeSpan.FromMinutes(1.5));

                await actor.Ask(new GetInstanceHashcode());
                await Task.Delay(TimeSpan.FromMinutes(1.5));

                Assert.AreEqual(hashcode, await actor.Ask(new GetInstanceHashcode()),
                    "Should prolong keepalive timeout after every external request using per-type specified timeout");
            }

            [Test]
            public async void When_reminder_request()
            {
                // global keepalive timeout is 1 minute,  per-type timeout is 2 minutes
                // we set reminder to fire in 1.5 minutes
                // we wait 1.5 minutes, and then 0.5 minutes are still left from per-type specified timeout
                // at that time reminder request shoull arrive, and should delay deactivation for another 2 minutes (per-type timeout)
                // if automatic keepalive prolongation doesn't work, an actor will still be alive for at least minute, as per global keepalive timeout
                // we wait again for 1.5 minutes to disprove previous assumption (that automatic prolongation is not working) 

                var actor = system.FreshActorOf<T>();
                var hashcode = await actor.Ask(new GetInstanceHashcode());

                await actor.Tell(new SetReminder {Period = TimeSpan.FromMinutes(1.5)});
                await Task.Delay(TimeSpan.FromMinutes(1.5));
                await Task.Delay(TimeSpan.FromMinutes(1.5));

                Assert.AreEqual(hashcode, await actor.Ask(new GetInstanceHashcode()),
                    "Should prolong keepalive timeout after every external request using per-type specified timeout");

                Assert.IsTrue(await actor.Ask(new HasBeenReminded()));
            }
        }

        [KeepAlive(Minutes = KeepAliveTimeoutInMinutes)]
        class TestKeepAliveDefinedViaAttributeActor : TestActor
        {}

        class TestKeepAliveDefinedViaPrototypeActor : TestActor
        {
            protected internal override void Define()
            {
                KeepAlive(TimeSpan.FromMinutes(KeepAliveTimeoutInMinutes));
            }
        }

        [Explicit, Category("Nightly")]
        class Defined_via_attribute
            : Tests<TestKeepAliveDefinedViaAttributeActor>
        {}

        [Explicit, Category("Nightly")]
        class Defined_via_prototype
            : Tests<TestKeepAliveDefinedViaPrototypeActor>
        {}
    }
}