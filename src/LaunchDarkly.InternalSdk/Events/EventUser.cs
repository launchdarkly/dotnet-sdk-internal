using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Used internally to represent user data that is being serialized in an <see cref="Event"/>.
    /// </summary>
    internal struct EventUser
    {
        public string Key { get; internal set; }
        public string Secondary { get; internal set; }
        public string IPAddress { get; internal set; }
        public string Country { get; internal set; }
        public string FirstName { get; internal set; }
        public string LastName { get; internal set; }
        public string Name { get; internal set; }
        public string Avatar { get; internal set; }
        public string Email { get; internal set; }
        public bool? Anonymous { get; internal set; }
        public IImmutableDictionary<string, LdValue> Custom { get; internal set; }
        public ImmutableSortedSet<string> PrivateAttrs { get; set; }

        public static EventUser FromUser(User user, EventsConfiguration config)
        {
            EventUserBuilder eub = new EventUserBuilder(user, config);
            return eub.Build();
        }
    }

    internal struct EventUserBuilder
    {
        private EventsConfiguration _config;
        private User _user;
        private EventUser _result;
        private ImmutableSortedSet<string>.Builder _privateAttrs;

        public EventUserBuilder(User user, EventsConfiguration config)
        {
            _user = user;
            _config = config;
            _result = new EventUser();
            _privateAttrs = null;
        }

        public EventUser Build()
        {
            _result.Key = _user.Key;
            _result.Secondary = StringAttrIfNotPrivate(UserAttribute.Secondary);
            _result.Anonymous = _user.Anonymous ? (bool?)true : null;
            _result.IPAddress = StringAttrIfNotPrivate(UserAttribute.IPAddress);
            _result.Country = StringAttrIfNotPrivate(UserAttribute.Country);
            _result.FirstName = StringAttrIfNotPrivate(UserAttribute.FirstName);
            _result.LastName = StringAttrIfNotPrivate(UserAttribute.LastName);
            _result.Name = StringAttrIfNotPrivate(UserAttribute.Name);
            _result.Avatar = StringAttrIfNotPrivate(UserAttribute.Avatar);
            _result.Email = StringAttrIfNotPrivate(UserAttribute.Email);

            // With the custom attributes, for efficiency's sake we would like to reuse the same ImmutableDictionary
            // whenever possible. So, we'll lazily create a new collection only if it turns out that there are any
            // changes needed (i.e. if one of the custom attributes turns out to be private).
            ImmutableDictionary<string, LdValue>.Builder customAttrsBuilder = null;
            foreach (var kv in _user.Custom)
            {
                if (!CheckPrivateAttr(UserAttribute.ForName(kv.Key)))
                {
                    if (customAttrsBuilder is null)
                    {
                        // This is the first private custom attribute we've found. Lazily create the builder
                        // by first copying all of the ones we've already iterated over. We can rely on the
                        // iteration order being the same because it's immutable.
                        customAttrsBuilder = ImmutableDictionary.CreateBuilder<string, LdValue>();
                        foreach (var kv1 in _user.Custom)
                        {
                            if (kv1.Key == kv.Key)
                            {
                                break;
                            }
                            customAttrsBuilder[kv1.Key] = kv1.Value;
                        }
                    }
                }
                else
                {
                    // It's not a private attribute.
                    if (customAttrsBuilder != null)
                    {
                        customAttrsBuilder[kv.Key] = kv.Value;
                    }
                }
            }
            var custom = customAttrsBuilder is null ? _user.Custom : customAttrsBuilder.ToImmutable();
            _result.Custom = custom.Count == 0 ? null : custom;
            _result.PrivateAttrs = _privateAttrs is null ? null : _privateAttrs.ToImmutable();
            return _result;
        }
        
        private bool CheckPrivateAttr(UserAttribute name)
        {
            if (_config.AllAttributesPrivate ||
                     (_config.PrivateAttributeNames != null &&_config.PrivateAttributeNames.Contains(name)) ||
                     (_user.PrivateAttributeNames != null && _user.PrivateAttributeNames.Contains(name.AttributeName)))
            {
                if (_privateAttrs is null)
                {
                    _privateAttrs = ImmutableSortedSet.CreateBuilder<string>();
                }
                _privateAttrs.Add(name.AttributeName);
                return false;
            }
            else
            {
                return true;
            }
        }

        private string StringAttrIfNotPrivate(UserAttribute attr)
        {
            var value = _user.GetAttribute(attr).AsString;
            return (value is null) ? null : (CheckPrivateAttr(attr) ? value : null);
        }
    }
}
