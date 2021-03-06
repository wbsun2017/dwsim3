﻿Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

Namespace DWSIM.Auxiliary.TaskSchedulers

    ' Provides a task scheduler that ensures a maximum concurrency level while  
    ' running on top of the thread pool. 
    Public Class LimitedConcurrencyLevelTaskScheduler : Inherits TaskScheduler
        ' Indicates whether the current thread is processing work items.
        <ThreadStatic()> Private Shared _currentThreadIsProcessingItems As Boolean

        ' The list of tasks to be executed  
        Private ReadOnly _tasks As LinkedList(Of Task) = New LinkedList(Of Task)()

        'The maximum concurrency level allowed by this scheduler.  
        Private ReadOnly _maxDegreeOfParallelism As Integer

        ' Indicates whether the scheduler is currently processing work items.  
        Private _delegatesQueuedOrRunning As Integer = 0 ' protected by lock(_tasks)

        ' Creates a new instance with the specified degree of parallelism.  
        Public Sub New(ByVal maxDegreeOfParallelism As Integer)
            If (maxDegreeOfParallelism < 1) Then
                Throw New ArgumentOutOfRangeException("maxDegreeOfParallelism")
            End If
            _maxDegreeOfParallelism = maxDegreeOfParallelism
        End Sub

        ' Queues a task to the scheduler.  
        Protected Overrides Sub QueueTask(ByVal t As Task)
            ' Add the task to the list of tasks to be processed.  If there aren't enough  
            ' delegates currently queued or running to process tasks, schedule another.  
            SyncLock (_tasks)
                _tasks.AddLast(t)
                If (_delegatesQueuedOrRunning < _maxDegreeOfParallelism) Then
                    _delegatesQueuedOrRunning = _delegatesQueuedOrRunning + 1
                    NotifyThreadPoolOfPendingWork()
                End If
            End SyncLock
        End Sub

        ' Inform the ThreadPool that there's work to be executed for this scheduler.  
        Private Sub NotifyThreadPoolOfPendingWork()

            ThreadPool.UnsafeQueueUserWorkItem(Sub()
                                                   ' Note that the current thread is now processing work items.  
                                                   ' This is necessary to enable inlining of tasks into this thread.
                                                   _currentThreadIsProcessingItems = True
                                                   Try
                                                       ' Process all available items in the queue.  
                                                       While (True)
                                                           Dim item As Task
                                                           SyncLock (_tasks)
                                                               ' When there are no more items to be processed,  
                                                               ' note that we're done processing, and get out.  
                                                               If (_tasks.Count = 0) Then
                                                                   _delegatesQueuedOrRunning = _delegatesQueuedOrRunning - 1
                                                                   Exit While
                                                               End If

                                                               ' Get the next item from the queue
                                                               item = _tasks.First.Value
                                                               _tasks.RemoveFirst()
                                                           End SyncLock

                                                           ' Execute the task we pulled out of the queue  
                                                           MyBase.TryExecuteTask(item)
                                                       End While
                                                       ' We're done processing items on the current thread  
                                                   Finally
                                                       _currentThreadIsProcessingItems = False
                                                   End Try
                                               End Sub,
                                          Nothing)
        End Sub

        ' Attempts to execute the specified task on the current thread.  
        Protected Overrides Function TryExecuteTaskInline(ByVal t As Task,
                                                          ByVal taskWasPreviouslyQueued As Boolean) As Boolean
            ' If this thread isn't already processing a task, we don't support inlining  
            If (Not _currentThreadIsProcessingItems) Then
                Return False
            End If

            ' If the task was previously queued, remove it from the queue  
            If (taskWasPreviouslyQueued) Then
                ' Try to run the task.  
                If TryDequeue(t) Then
                    Return MyBase.TryExecuteTask(t)
                Else
                    Return False
                End If
            Else
                Return MyBase.TryExecuteTask(t)
            End If
        End Function

        ' Attempt to remove a previously scheduled task from the scheduler.  
        Protected Overrides Function TryDequeue(ByVal t As Task) As Boolean
            SyncLock (_tasks)
                Return _tasks.Remove(t)
            End SyncLock
        End Function

        ' Gets the maximum concurrency level supported by this scheduler.  
        Public Overrides ReadOnly Property MaximumConcurrencyLevel As Integer
            Get
                Return _maxDegreeOfParallelism
            End Get
        End Property

        ' Gets an enumerable of the tasks currently scheduled on this scheduler.  
        Protected Overrides Function GetScheduledTasks() As IEnumerable(Of Task)
            Dim lockTaken As Boolean = False
            Try
                Monitor.TryEnter(_tasks, lockTaken)
                If (lockTaken) Then
                    Return _tasks.ToArray()
                Else
                    Throw New NotSupportedException()
                End If
            Finally
                If (lockTaken) Then
                    Monitor.Exit(_tasks)
                End If
            End Try
        End Function
    End Class

End Namespace