﻿//            Copyright Keysight Technologies 2012-2019
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, you can obtain one at http://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTap
{
    /// <summary>
    /// Base info for reflection objects.
    /// </summary>
    public interface IReflectionData
    {
        /// <summary> The attributes of it. </summary>
        IEnumerable<object> Attributes { get; }
        /// <summary>
        /// The name of it.
        /// </summary>
        string Name { get; }
    }

    /// <summary> A member of an object type. </summary>
    public interface IMemberData : IReflectionData
    {
        /// <summary> The type on which this member is declared. </summary>
        ITypeData DeclaringType { get; }
        /// <summary> The underlying type of this member. </summary>
        ITypeData TypeDescriptor { get; }
        /// <summary> Gets if this member is writable. </summary>
        bool Writable { get; }
        /// <summary> Gets if this member is readable.</summary>
        bool Readable { get; }
        /// <summary> Sets the value of this member on the owner. </summary>
        /// <param name="owner"></param>
        /// <param name="value"></param>
        void SetValue(object owner, object value);
        /// <summary>
        /// Gets the value of this member on the owner.
        /// </summary>
        /// <param name="owner"></param>
        /// <returns></returns>
        object GetValue(object owner);
    }

    /// <summary> The type information of an object. </summary>
    public interface ITypeData : IReflectionData
    {
        /// <summary> The base type of this type. </summary>
        ITypeData BaseType { get; }
        /// <summary> Gets the members of this object. </summary>
        /// <returns></returns>
        IEnumerable<IMemberData> GetMembers();
        /// <summary> Gets a member of this object by name.  </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        IMemberData GetMember(string name);
        /// <summary>
        /// Creates an instance of this type. The arguments are used for construction.
        /// </summary>
        /// <param name="arguments"></param>
        /// <returns></returns>
        object CreateInstance(object[] arguments);
        /// <summary>
        /// Gets if CreateInstance will work for this type. For examples, for interfaces and abstract classes it will not work.
        /// </summary>
        bool CanCreateInstance { get; }
    }

    /// <summary>Hook into type reflection system. Provides type data for a given object or identifier. </summary>
    [Display("TypeData Provider")]
    public interface ITypeDataProvider : ITapPlugin
    {
        /// <summary> Gets the type data from an identifier. </summary>
        /// <param name="identifier">The identifier to get type information for.</param>
        /// <returns>A representation of the type specified by identifier or null if this provider cannot handle the specified identifier.</returns>
        ITypeData GetTypeData(string identifier);

        /// <summary> Gets the type data from an object. </summary>
        /// <param name="obj">The object to get type information for.</param>
        /// <returns>A representation of the type of the specified object or null if this provider cannot handle the specified type of object.</returns>
        ITypeData GetTypeData(object obj);

        /// <summary> The priority of this type info provider. Note, this decides the order in which the type info is resolved. </summary>
        double Priority { get; }
    }

    /// <summary>Hook into type reflection system. Provides type data for a given object or identifier. This variant is aware of the stack of other providers running after itself.</summary>
    [Display("Stacked TypeData Provider")]
    public interface IStackedTypeDataProvider : ITapPlugin
    {
        /// <summary> Gets the type data from an identifier. </summary>
        /// <param name="identifier">The identifier to get type information for.</param>
        /// <param name="stack">Stack containing remaining ITypeDataProviders that have not yet been called.</param>
        /// <returns>A representation of the type specified by identifier or null if this provider cannot handle the specified identifier.</returns>
        ITypeData GetTypeData(string identifier, TypeDataProviderStack stack);

        /// <summary> Gets the type data from an object. </summary>
        /// <param name="obj">The object to get type information for.</param>
        /// <param name="stack">Stack containing remaining ITypeDataProviders that have not yet been called.</param>
        /// <returns>A representation of the type of the specified object or null if this provider cannot handle the specified type of object.</returns>
        ITypeData GetTypeData(object obj, TypeDataProviderStack stack);

        /// <summary> The priority of this type info provider. Note, this decides the order in which the type info is resolved. </summary>
        double Priority { get; }
    }

    /// <summary> 
    /// Represents a stack of ITypeDataProvider/IStackedTypeDataProvider that is used to get TypeData for a given type. 
    /// The providers on this stack are called in order until a provider returuns a
    /// </summary>
    public class TypeDataProviderStack
    {
        List<object> providers;
        int offset = 0;

        internal TypeDataProviderStack()
        {
            offset = 0;
            providers = GetProviders();
        }

        private TypeDataProviderStack(List<object> providers, int providerOffset)
        {
            this.providers = providers;
            this.offset = providerOffset;
        }

        /// <summary> Gets the type data from an object. </summary>
        /// <param name="obj">The object to get type information for.</param>
        /// <returns>A representation of the type of the specified object or null if no providers can handle the specified type of object.</returns>
        public ITypeData GetTypeData(object obj)
        {
            while (offset < providers.Count)
            {
                var provider = providers[offset];
                offset++;
                if (provider is IStackedTypeDataProvider sp)
                {
                    var newStack = new TypeDataProviderStack(providers, offset);
                    if (sp.GetTypeData(obj, newStack) is ITypeData found)
                        return found;
                }
                else if (provider is ITypeDataProvider p)
                {
                    if (p.GetTypeData(obj) is ITypeData found)
                        return found;
                }
            }
            return null;
        }

        /// <summary> Gets the type data from an identifier. </summary>
        /// <param name="identifier">The identifier to get type information for.</param>
        /// <returns>A representation of the type specified by identifier or null if no providers can handle the specified identifier.</returns>
        public ITypeData GetTypeData(string identifier)
        {
            while (offset < providers.Count)
            {
                var provider = providers[offset];
                offset++;
                if (provider is IStackedTypeDataProvider sp)
                {
                    var newStack = new TypeDataProviderStack(providers, offset);
                    if (sp.GetTypeData(identifier, newStack) is ITypeData found)
                        return found;
                }
                else if (provider is ITypeDataProvider p)
                {
                    if (p.GetTypeData(identifier) is ITypeData found)
                        return found;
                }
            }
            return null;
        }

        static List<object> providersCache = new List<object>();
        static List<object> GetProviders()
        {
            var _providers = TypeData.FromType(typeof(ITypeDataProvider)).DerivedTypes;
            _providers = _providers.Concat(TypeData.FromType(typeof(IStackedTypeDataProvider)).DerivedTypes);
            if (providersCache.Count == _providers.Count()) return providersCache;
            providersCache = _providers.Select(x => x.CreateInstanceSafe())
                                       .OrderByDescending(x => (x as ITypeDataProvider)?.Priority ?? (x as IStackedTypeDataProvider).Priority)
                                       .ToList();
            return providersCache;
        }
    }

    /// <summary> Helpers for work with ITypeInfo objects. </summary>
    public static class ReflectionDataExtensions
    {
        /// <summary> returns true if 'type' is a descendant of 'basetype'. </summary>
        /// <param name="type"></param>
        /// <param name="basetype"></param>
        /// <returns></returns>
        public static bool DescendsTo(this ITypeData type, ITypeData basetype)
        {
            if (basetype is TypeData basetype2)
            {
                return DescendsTo(type, basetype2.Type);
            }
            while (type != null)
            {    
                if (object.Equals(type, basetype))
                    return true;
                type = type.BaseType;
            }
            return false;
        }
        /// <summary> returns tru if 'type' is a descendant of 'basetype'. </summary>
        /// <param name="type"></param>
        /// <param name="basetype"></param>
        /// <returns></returns>
        public static bool DescendsTo(this ITypeData type, Type basetype)
        {
            while (type != null)
            {
                if (type is TypeData cst)
                {
                    return cst.Type.DescendsTo(basetype);
                }

                type = type.BaseType;
            }
            return false;
        }
        /// <summary>
        /// Returns true if a reflection ifno has an attribute of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mem"></param>
        /// <returns></returns>
        static public bool HasAttribute<T>(this IReflectionData mem) where T: class
        {
            return mem.GetAttribute<T>() != null;
        }

        /// <summary> Gets the attribute of type T from mem. </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mem"></param>
        /// <returns></returns>
        static public T GetAttribute<T>(this IReflectionData mem)
        {
            if (mem.Attributes is object[] array)
            {
                // performance optimization: faster iterations if we know its an array.
                foreach (var thing in array)
                    if (thing is T x)
                        return x;
            }
            else
            {
                foreach (var thing in mem.Attributes)
                    if (thing is T x)
                        return x;
            }

            return default;
        }

        /// <summary> Gets all the attributes of type T.</summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mem"></param>
        /// <returns></returns>
        static public IEnumerable<T> GetAttributes<T>(this IReflectionData mem)
        {
            return mem.Attributes.OfType<T>();
        }

        /// <summary> Gets the display attribute of mem. </summary>
        /// <param name="mem"></param>
        /// <returns></returns>
        public static DisplayAttribute GetDisplayAttribute(this IReflectionData mem)
        {
            DisplayAttribute attr = null;
            if (mem is TypeData td)
                attr = td.Display;
            else
                attr = mem.GetAttribute<DisplayAttribute>();
            return attr ?? new DisplayAttribute(mem.Name, null, Order: -10000, Collapsed: false);
        }

        /// <summary>Gets the help link of 'member'</summary>
        /// <param name="member"></param>
        /// <returns></returns>
        internal static HelpLinkAttribute GetHelpLink(this IReflectionData member)
        {
            var attr = member.GetAttribute<HelpLinkAttribute>();
            if (attr != null)
                return attr;
            if (member is IMemberData meminfo)// Recursively look for class level help.
                return meminfo.DeclaringType.GetHelpLink();
            return null;
        }
    }
}
