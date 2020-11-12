﻿using System;
using LaunchDarkly.Sdk.Internal.Helpers;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventFactoryTest
    {
        private static readonly User user = User.WithKey("user-key");
        private static readonly LdValue resultVal = LdValue.Of("result");
        private static readonly LdValue defaultVal = LdValue.Of("default");

        private long TimeNow()
        {
            return Util.GetUnixTimestampMillis(DateTime.UtcNow);
        }

        [Fact]
        public void EventFactoryGetsTimestamp()
        {
            var time0 = TimeNow();
            var time1 = EventFactory.Default.GetTimestamp();
            var time2 = EventFactory.DefaultWithReasons.GetTimestamp();
            Assert.NotEqual(0, time0);
            Assert.True(time1 >= time0);
            Assert.True(time2 >= time1);
        }

        [Fact]
        public void FeatureEventHasBasicProperties()
        {
            var time = TimeNow();
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewFeatureRequestEvent(flag, user, result, defaultVal);
            Assert.True(e.CreationDate >= time);
            Assert.Equal(flag.Key, e.Key);
            Assert.Same(user, e.User);
            Assert.Equal(flag.EventVersion, e.Version);
            Assert.Equal(result.VariationIndex, e.Variation);
            Assert.Equal(result.Value, e.Value);
            Assert.Equal(defaultVal, e.Default);
            Assert.Null(e.PrereqOf);
            Assert.Null(e.Reason);
            Assert.False(e.TrackEvents);
            Assert.Null(e.DebugEventsUntilDate);
        }

        [Fact]
        public void FeatureEventUsesTrackAndDebugPropertiesFromFlag()
        {
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100)
                .TrackEvents(true).DebugEventsUntilDate(1000).Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewFeatureRequestEvent(flag, user, result, defaultVal);
            Assert.True(e.TrackEvents);
            Assert.Equal(1000, e.DebugEventsUntilDate);
        }

        [Fact]
        public void FeatureEventHasReasonWhenUsingFactoryWithReason()
        {
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.DefaultWithReasons.NewFeatureRequestEvent(flag, user, result, defaultVal);
            Assert.Equal(result.Reason, e.Reason);
        }

        [Fact]
        public void FeatureEventHasTrackEventsAndReasonForExperiment()
        {
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100)
                .ExperimentReason(EvaluationReason.FallthroughReason)
                .Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewFeatureRequestEvent(flag, user, result, defaultVal);
            Assert.Equal(result.Reason, e.Reason);
            Assert.True(e.TrackEvents);
        }

        [Fact]
        public void DefaultFeatureEventHasBasicProperties()
        {
            var time = TimeNow();
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var err = EvaluationErrorKind.EXCEPTION;
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewDefaultFeatureRequestEvent(flag, user, defaultVal, err);
            Assert.True(e.CreationDate >= time);
            Assert.Equal(flag.Key, e.Key);
            Assert.Same(user, e.User);
            Assert.Equal(flag.EventVersion, e.Version);
            Assert.Null(e.Variation);
            Assert.Equal(defaultVal, e.Value);
            Assert.Equal(defaultVal, e.Default);
            Assert.Null(e.PrereqOf);
            Assert.Null(e.Reason);
            Assert.False(e.TrackEvents);
            Assert.Null(e.DebugEventsUntilDate);
        }

        [Fact]
        public void DefaultFeatureEventUsesTrackAndDebugPropertiesFromFlag()
        {
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100)
                .TrackEvents(true).DebugEventsUntilDate(1000).Build();
            var err = EvaluationErrorKind.EXCEPTION;
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewDefaultFeatureRequestEvent(flag, user, defaultVal, err);
            Assert.True(e.TrackEvents);
            Assert.Equal(1000, e.DebugEventsUntilDate);
        }

        [Fact]
        public void DefaultFeatureEventHasReasonWhenUsingFactoryWithReason()
        {
            var flag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var err = EvaluationErrorKind.EXCEPTION;
            var e = EventFactory.DefaultWithReasons.NewDefaultFeatureRequestEvent(flag, user, defaultVal, err);
            Assert.Equal(EvaluationReason.ErrorReason(err), e.Reason);
        }

        [Fact]
        public void UnknownFeatureEventHasBasicProperties()
        {
            var time = TimeNow();
            var err = EvaluationErrorKind.FLAG_NOT_FOUND;
            var e = EventFactory.Default.NewUnknownFeatureRequestEvent("flag-key", user, defaultVal, err);
            Assert.True(e.CreationDate >= time);
            Assert.Equal("flag-key", e.Key);
            Assert.Same(user, e.User);
            Assert.Null(e.Version);
            Assert.Null(e.Variation);
            Assert.Equal(defaultVal, e.Value);
            Assert.Equal(defaultVal, e.Default);
            Assert.Null(e.PrereqOf);
            Assert.Null(e.Reason);
            Assert.False(e.TrackEvents);
            Assert.Null(e.DebugEventsUntilDate);
        }

        [Fact]
        public void UnknownFeatureEventHasReasonWhenUsingFactoryWithReason()
        {
            var err = EvaluationErrorKind.FLAG_NOT_FOUND;
            var e = EventFactory.DefaultWithReasons.NewUnknownFeatureRequestEvent("flag-key", user, defaultVal, err);
            Assert.Equal(EvaluationReason.ErrorReason(err), e.Reason);
        }
        
        [Fact]
        public void PrerequisiteFeatureEventHasBasicProperties()
        {
            var time = TimeNow();
            var parentFlag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var flag = new FlagEventPropertiesBuilder("prereq-key").Version(100).Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewPrerequisiteFeatureRequestEvent(flag, user, result, parentFlag);
            Assert.True(e.CreationDate >= time);
            Assert.Equal("prereq-key", e.Key);
            Assert.Same(user, e.User);
            Assert.Equal(flag.EventVersion, e.Version);
            Assert.Equal(result.VariationIndex, e.Variation);
            Assert.Equal(result.Value, e.Value);
            Assert.Equal(LdValue.Null, e.Default);
            Assert.Equal("flag-key", e.PrereqOf);
            Assert.Null(e.Reason);
            Assert.False(e.TrackEvents);
            Assert.Null(e.DebugEventsUntilDate);
        }
        
        [Fact]
        public void PrerequisiteFeatureEventHasReasonWhenUsingFactoryWithReason()
        {
            var parentFlag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var flag = new FlagEventPropertiesBuilder("prereq-key").Version(100).Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.DefaultWithReasons.NewPrerequisiteFeatureRequestEvent(flag, user, result, parentFlag);
            Assert.Equal(result.Reason, e.Reason);
        }

        [Fact]
        public void PrerequisiteFeatureEventHasTrackEventsAndReasonForExperiment()
        {
            var parentFlag = new FlagEventPropertiesBuilder("flag-key").Version(100).Build();
            var flag = new FlagEventPropertiesBuilder("prereq-key").Version(100)
                .ExperimentReason(EvaluationReason.FallthroughReason)
                .Build();
            var result = new EvaluationDetail<LdValue>(resultVal, 1, EvaluationReason.FallthroughReason);
            var e = EventFactory.Default.NewPrerequisiteFeatureRequestEvent(flag, user, result, parentFlag);
            Assert.Equal(result.Reason, e.Reason);
            Assert.True(e.TrackEvents);
        }

        [Fact]
        public void CustomEventHasBasicProperties()
        {
            var time = TimeNow();
            var data = LdValue.Of("hi");
            var e = EventFactory.Default.NewCustomEvent("yay", user, data, 1.5);
            Assert.True(e.CreationDate >= time);
            Assert.Equal("yay", e.Key);
            Assert.Same(user, e.User);
            Assert.Equal(data, e.Data);
            Assert.Equal(1.5, e.MetricValue);
        }

        [Fact]
        public void CustomEventCanOmitData()
        {
            var e = EventFactory.Default.NewCustomEvent("yay", user, LdValue.Null, null);
            Assert.Equal(LdValue.Null, e.Data);
            Assert.Null(e.MetricValue);
        }

        [Fact]
        public void IdentifyEventHasBasicProperties()
        {
            var time = TimeNow();
            var e = EventFactory.Default.NewIdentifyEvent(user);
            Assert.True(e.CreationDate >= time);
            Assert.Same(user, e.User);
        }
    }
}
