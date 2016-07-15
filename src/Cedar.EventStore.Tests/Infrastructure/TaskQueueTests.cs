﻿namespace Cedar.EventStore.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;
    using Xunit.Abstractions;

    public class TaskQueueTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public TaskQueueTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task Enqueued_tasks_should_be_executed()
        {
            using (var taskQueue = new TaskQueue())
            {
                var tasks = new ConcurrentBag<Task>();

                for (int i = 0; i < 1000; i++)
                {
                    var task = taskQueue.Enqueue(async ct =>
                    {
                        await Task.Delay(1, ct);
                        _testOutputHelper.WriteLine(i.ToString());
                    });
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
            }
        }

        [Fact]
        public async Task Multi_threaded_enqueued_tasks_should_be_executed()
        {
            using(var taskQueue = new TaskQueue())
            {
                var tasks = new ConcurrentBag<Task>();

                Parallel.For(0,
                    250,
                    i =>
                    {
                        var task = taskQueue.Enqueue(async ct =>
                        {
                            await Task.Delay(1, ct);
                            _testOutputHelper.WriteLine(i.ToString());
                        });
                        tasks.Add(task);
                    });

                await Task.WhenAll(tasks);
            }
        }

        [Fact]
        public void When_disposed_then_enqueued_task_should_be_cancelled()
        {
            var taskQueue = new TaskQueue();
            taskQueue.Dispose();

            var task = taskQueue.Enqueue(() => {});

            task.IsCanceled.ShouldBeTrue();
        }

        [Fact]
        public async Task When_disposed_then_queued_tasks_should_be_cancelled()
        {
            var block = new ManualResetEventSlim();
            var taskQueue = new TaskQueue();
            var blockingTask = taskQueue.Enqueue(() =>
            {
                block.Wait();
            });

            int firstTaskToComplete = 0;
            int taskNumber = 1;
            int highPriorityTaskNumber = 2;
            var task = taskQueue.Enqueue(() =>
            {
                Interlocked.CompareExchange(ref firstTaskToComplete, taskNumber, 0);
            });
            var highPriorityTask = taskQueue.EnqueueHighPriority(() =>
            {
                Interlocked.CompareExchange(ref firstTaskToComplete, highPriorityTaskNumber, 0);
            });
            block.Set();

            await Task.WhenAll(blockingTask, task, highPriorityTask);

            firstTaskToComplete.ShouldBe(highPriorityTaskNumber);
        }

        [Fact]
        public async Task When_enqueued_function_throws_then_should_propagate_exception()
        {
            using(var taskQueue = new TaskQueue())
            {
                var task = taskQueue.Enqueue(() =>
                {
                    throw new InvalidOperationException();
                });

                Func<Task> act = async () => await task;

                await act.ShouldThrowAsync<InvalidOperationException>();
            }
        }

        [Fact]
        public async Task When_enqueued_function_cancels_then_should_propagate_exception()
        {
            using (var taskQueue = new TaskQueue())
            {
                var queuedTask = taskQueue.Enqueue(() =>
                {
                    throw new TaskCanceledException();
                });

                Exception exception = null;
                try
                {
                    await queuedTask;
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                exception.ShouldBeOfType<TaskCanceledException>();
            }
        }

        [Fact]
        public async Task High_priority_tasks_should_take_precedenc()
        {
            var block = new ManualResetEventSlim();
            var taskQueue = new TaskQueue();
            var blockingTask = taskQueue.Enqueue(() =>
            {
                block.Wait();
            });
            var queuedTask = taskQueue.Enqueue(() => {});
            taskQueue.Dispose();
            block.Set();

            Exception exception = null;
            try
            {
                await queuedTask;
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            exception.ShouldBeOfType<TaskCanceledException>();
        }
    }
}