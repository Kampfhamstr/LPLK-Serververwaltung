import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('health', Path(__file__).parents[1] / 'scripts/host-health/lplk_health.py')
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


class HealthChecks(unittest.TestCase):
    def setUp(self):
        self.now = 10000
        self.sample = {'server': '11110003-0000-4000-8000-000000000003', 'running': True,
                       'started_at': 9000, 'bridge_age': 2, 'sources_complete': True,
                       'swiftly': '1.4.14', 'cs2': '123', 'crashes': []}

    def test_empty_running_never_means_accepted(self):
        public, _ = h.evaluate(self.sample, {}, self.now)
        self.assertEqual(public['state'], 'observing')
        self.assertEqual(public['player_acceptance'], 'unconfirmed')
        self.assertFalse(public['automatic_changes'])

    def test_crashes_are_deduplicated(self):
        self.sample['crashes'] = [{'id': 'a', 'at': 9990, 'swiftly': '1.4.14'}]
        first, saved = h.evaluate(self.sample, {}, self.now)
        second, _ = h.evaluate(self.sample, saved, self.now + 10)
        self.assertEqual(first['state'], 'crashed')
        self.assertEqual(second['crashes_10m'], 1)
        self.assertEqual(second['state'], 'observing')

    def test_three_crashes_even_with_exit_zero(self):
        self.sample['crashes'] = [{'id': str(i), 'at': 9990 - i, 'swiftly': '1.4.14'} for i in range(3)]
        public, _ = h.evaluate(self.sample, {}, self.now)
        self.assertEqual(public['state'], 'crash_loop')

    def test_old_beta_dumps_after_recovery_are_not_new_stable_crash(self):
        self.sample['crashes'] = [{'id': str(i), 'at': 9990 - i, 'swiftly': '1.5.0-beta.11'} for i in range(3)]
        public, _ = h.evaluate(self.sample, {}, self.now)
        self.assertEqual(public['crashes_10m'], 0)
        self.assertEqual(public['state'], 'observing')

    def test_late_stale_and_missing_are_not_healthy(self):
        for age in [None, 91, -16]:
            with self.subTest(age=age):
                self.sample['bridge_age'] = age
                self.assertEqual(h.evaluate(self.sample, {}, self.now)[0]['state'], 'stale')

    def test_start_grace_and_manual_stop(self):
        self.sample.update(started_at=9990, bridge_age=None)
        self.assertEqual(h.evaluate(self.sample, {}, self.now)[0]['state'], 'starting')
        self.sample['running'] = False
        self.assertEqual(h.evaluate(self.sample, {}, self.now)[0]['state'], 'offline')

    def test_unknown_sources(self):
        self.sample['sources_complete'] = False
        self.assertEqual(h.evaluate(self.sample, {}, self.now)[0]['state'], 'unknown')

    def test_candidate_requires_matching_cs2_and_canary(self):
        current = {'cs2': 'new', 'swiftly': '1.4.14'}
        candidate = {'version': '1.5.0-beta.12', 'channel': 'prerelease', 'cs2': 'old', 'canary_passed': True}
        self.assertEqual(h.choose_candidate(current, [candidate], [], True, False)['action'], 'wait_for_verified_candidate')
        candidate.update(cs2='new', canary_passed=False)
        self.assertEqual(h.choose_candidate(current, [candidate], [], True, False)['action'], 'wait_for_verified_candidate')
        candidate['canary_passed'] = True
        self.assertEqual(h.choose_candidate(current, [candidate], [], True, False)['channel'], 'prerelease')
        self.assertEqual(h.choose_candidate(current, [candidate], [[candidate['version'], 'new']], True, False)['action'], 'wait_for_verified_candidate')
        self.assertEqual(h.choose_candidate(current, [candidate], [], False, False)['action'], 'wait_for_verified_candidate')

    def test_egg_downgrade_limit(self):
        current = {'cs2': 'new', 'swiftly': '1.5.0-beta.11'}
        candidate = {'version': '1.4.14', 'channel': 'stable', 'cs2': 'new', 'canary_passed': True}
        self.assertEqual(h.choose_candidate(current, [candidate], [], False, False)['action'], 'explicit_downgrade_required')

    def test_missing_versions_cannot_authorize_candidate(self):
        self.assertEqual(h.choose_candidate({'cs2': None, 'swiftly': None}, [], [], True, True)['action'], 'wait_for_version_evidence')

    def test_state_bounded_and_expired(self):
        previous = {'crashes': [{'id': str(i), 'at': i, 'swiftly': '1.4.14'} for i in range(200)], 'version_changes': [{}] * 40}
        _, saved = h.evaluate(self.sample, previous, self.now)
        self.assertLessEqual(len(saved['crashes']), 128)
        self.assertLessEqual(len(saved['version_changes']), 30)


if __name__ == '__main__':
    unittest.main()
