﻿// ******************************************************************
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THE CODE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE CODE OR THE USE OR OTHER DEALINGS IN THE CODE.
// ******************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace Microsoft.Toolkit.Uwp
{
    /// <summary>
    /// This class represents an <see cref="ObservableCollection{IType}"/> whose items can be loaded incrementally.
    /// </summary>
    /// <typeparam name="TSource">
    /// The data source that must be loaded incrementally.
    /// </typeparam>
    /// <typeparam name="IType">
    /// The type of collection items.
    /// </typeparam>
    /// <seealso cref="IIncrementalSource{TSource}"/>
    /// <seealso cref="ISupportIncrementalLoading"/>
    public class IncrementalLoadingCollection<TSource, IType> : ObservableCollection<IType>,
         ISupportIncrementalLoading
         where TSource : IIncrementalSource<IType>
    {
        /// <summary>
        /// Gets a value indicating the source of incremental loading.
        /// </summary>
        protected TSource Source { get; }

        /// <summary>
        /// Gets a value indicating how many items that must be retrieved for each incremental call.
        /// </summary>
        protected int ItemsPerPage { get; }

        /// <summary>
        /// Gets or sets a value indicating The zero-based index of the current items page.
        /// </summary>
        protected int CurrentPageIndex { get; set; }

        private readonly Action _onStartLoading;
        private readonly Action _onEndLoading;
        private readonly Action<Exception> _onError;

        private bool _isLoading;
        private bool _hasMoreItems;
        private CancellationToken _cancellationToken;

        /// <summary>
        /// Gets a value indicating whether new items are being loaded.
        /// </summary>
        public bool IsLoading
        {
            get
            {
                return _isLoading;
            }

            private set
            {
                if (value != _isLoading)
                {
                    _isLoading = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsLoading)));

                    if (_isLoading)
                    {
                        _onStartLoading?.Invoke();
                    }
                    else
                    {
                        _onEndLoading?.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the collection contains more items to retrieve.
        /// </summary>
        public bool HasMoreItems
        {
            get
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                return _hasMoreItems;
            }

            private set
            {
                if (value != _hasMoreItems)
                {
                    _hasMoreItems = value;
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasMoreItems)));
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IncrementalLoadingCollection{TSource, IType}"/> class optionally specifying how many items to load for each data page.
        /// </summary>
        /// <param name="itemsPerPage">
        /// The number of items to retrieve for each call. Default is 20.
        /// </param>
        /// <param name="onStartLoading">
        /// An <see cref="Action"/> that is called when a retrieval operation begins.
        /// </param>
        /// <param name="onEndLoading">
        /// An <see cref="Action"/> that is called when a retrieval operation ends.
        /// </param>
        /// <param name="onError">
        /// An <see cref="Action"/> that is called if an error occours during data retrieval.
        /// </param>
        /// <seealso cref="IIncrementalSource{TSource}"/>
        public IncrementalLoadingCollection(int itemsPerPage = 20, Action onStartLoading = null, Action onEndLoading = null, Action<Exception> onError = null)
            : this(InstantiateSourceByReflection(), itemsPerPage, onStartLoading, onEndLoading, onError)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IncrementalLoadingCollection{TSource, IType}"/> class using the specified <see cref="IIncrementalSource{TSource}"/> implementation and, optionally, how many items to load for each data page.
        /// </summary>
        /// <param name="source">
        /// An implementation of the <see cref="IIncrementalSource{TSource}"/> interface that contains the logic to actually load data incrementally.
        /// </param>
        /// <param name="itemsPerPage">
        /// The number of items to retrieve for each call. Default is 20.
        /// </param>
        /// <param name="onStartLoading">
        /// An <see cref="Action"/> that is called when a retrieval operation begins.
        /// </param>
        /// <param name="onEndLoading">
        /// An <see cref="Action"/> that is called when a retrieval operation ends.
        /// </param>
        /// <param name="onError">
        /// An <see cref="Action"/> that is called if an error occours during data retrieval.
        /// </param>
        /// <seealso cref="IIncrementalSource{TSource}"/>
        public IncrementalLoadingCollection(TSource source, int itemsPerPage = 20, Action onStartLoading = null, Action onEndLoading = null, Action<Exception> onError = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Source = source;

            _onStartLoading = onStartLoading;
            _onEndLoading = onEndLoading;
            _onError = onError;

            ItemsPerPage = itemsPerPage;
            _hasMoreItems = true;
        }

        /// <summary>
        /// Initializes incremental loading from the view.
        /// </summary>
        /// <param name="count">
        /// The number of items to load.
        /// </param>
        /// <returns>
        /// An object of the <see cref="LoadMoreItemsAsync(uint)"/> that specifies how many items have been actually retrieved.
        /// </returns>
        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
            => AsyncInfo.Run((c) => LoadMoreItemsAsync(count, c));

        /// <summary>
        /// Actually performs the incremental loading.
        /// </summary>
        /// <param name="cancellationToken">
        /// Used to propagate notification that operation should be canceled.
        /// </param>
        /// <returns>
        /// Returns a collection of <typeparamref name="IType"/>.
        /// </returns>
        protected virtual async Task<IEnumerable<IType>> LoadDataAsync(CancellationToken cancellationToken)
        {
            var result = await Source.GetPagedItemsAsync(CurrentPageIndex++, ItemsPerPage, cancellationToken);
            return result;
        }

        private static TSource InstantiateSourceByReflection()
        {
            var type = typeof(TSource);
            ConstructorInfo constructor = type.GetConstructor(new Type[0]);
            if (constructor == null)
            {
                throw new InvalidOperationException("TSource must have a parameterless constructor");
            }

            return (TSource)constructor.Invoke(null);
        }

        private async Task<LoadMoreItemsResult> LoadMoreItemsAsync(uint count, CancellationToken cancellationToken)
        {
            uint resultCount = 0;
            _cancellationToken = cancellationToken;

            try
            {
                if (!_cancellationToken.IsCancellationRequested)
                {
                    IEnumerable<IType> data = null;
                    try
                    {
                        IsLoading = true;
                        data = await LoadDataAsync(_cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // The operation has been canceled using the Cancellation Token.
                    }
                    catch (Exception ex) when (_onError != null)
                    {
                        _onError.Invoke(ex);
                    }

                    if (data != null && data.Any() && !_cancellationToken.IsCancellationRequested)
                    {
                        var dispatcher = Window.Current.Dispatcher;
                        resultCount = (uint)data.Count();

                        await dispatcher.RunAsync(
                            CoreDispatcherPriority.Normal,
                            () =>
                            {
                                foreach (var item in data)
                                {
                                    Add(item);
                                }
                            });
                    }
                    else
                    {
                        HasMoreItems = false;
                    }
                }
            }
            finally
            {
                IsLoading = false;
            }

            return new LoadMoreItemsResult { Count = resultCount };
        }
    }
}
