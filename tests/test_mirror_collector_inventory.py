"""Internal ScCollector ownership/lifetime guards, with synthetic NX state."""
from types import SimpleNamespace as NS
import unittest

from test_mirror_map_inputs import M, CollectorWorld, SelectionIntentRule


class CollectorInventoryTests(unittest.TestCase):
    def world(self):
        h=CollectorWorld();h.rules=[SelectionIntentRule(h)];h.filters[10]=True
        return h

    def original_untouched(self,h):
        h.collector.RemoveEvaluationFilter.assert_not_called();h.collector.Destroy.assert_not_called()
        h.w.replace.Commit.assert_not_called();h.t.setup.DeleteToolPath.assert_not_called()
        h.t.transfer_mcs_and_stock.assert_not_called()

    def rejected(self,h,message='.*',exception=RuntimeError):
        before=h.snapshot()
        with self.assertRaisesRegex(exception,message) as caught:h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.original_untouched(h)
        return caught.exception

    def test_report_internal_original_and_clones_need_not_appear_in_public_collection(self):
        h=self.world();h.t.part.ScCollectors.clear()
        h.after_clone=lambda c:h.t.part.ScCollectors.remove(c)
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.assertEqual(h.marks,{})
        self.original_untouched(h)
        self.assertGreater(len(h.clones),2)
        for c in h.clones:
            c.RemoveEvaluationFilter.assert_called_once_with(10);c.Destroy.assert_called_once()
        report='\n'.join(h.t.lines)
        self.assertIn('AskRulesAndCollectedEntities; только BodyDumb; counts=(1,)',report)
        self.assertIn('исходная коллекция=170651; найдена=True',report)
        self.assertIn('временная коллекция удалена',report)

    def test_empty_database_cannot_be_interpreted_as_a_complete_inventory(self):
        h=self.world();h.t.native.object_inventory.side_effect=lambda *a:(frozenset(),frozenset())
        self.rejected(h,'Исходная ScCollector 170651 не найдена')
        h.collector.CopyCollector.assert_not_called()
        self.assertIn('объектов=0; внутренних контейнеров=0','\n'.join(h.t.lines))

    def test_public_membership_does_not_replace_missing_database_membership(self):
        h=self.world()
        h.t.native.object_inventory.side_effect=lambda *a:(frozenset(h.w.objects),frozenset())
        self.rejected(h,'не найдена в базе копии')
        h.collector.CopyCollector.assert_not_called()

    def test_not_fully_loaded_copy_fails_before_native_enumeration(self):
        for value in (False,None,1):
            h=self.world();h.t.part.IsFullyLoaded=value
            with self.subTest(value=value):self.rejected(h,'полностью загружена')
            h.t.native.object_inventory.assert_not_called();h.collector.CopyCollector.assert_not_called()

    def test_missing_installed_type_constant_is_not_replaced_with_a_guess(self):
        h=self.world();del h.t.nx.UF.UFConstants.UF_smart_container_type
        self.rejected(h,'UF_smart_container_type',AttributeError)
        h.t.native.object_inventory.assert_not_called();h.collector.CopyCollector.assert_not_called()

    def test_database_error_keeps_api_code_and_does_not_fall_back_to_factory_collection(self):
        h=self.world();error=M.NativeApiError(7501,'UF_OBJ_ask_type_and_subtype','inventory failed')
        h.t.native.object_inventory.side_effect=error
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        h.collector.CopyCollector.assert_not_called()

    def test_part_change_during_database_read_prevents_copy_and_other_part_undo(self):
        h=self.world();foreign=NS(Tag=9999)
        def changed(*args):
            result=h.object_inventory(*args);h.t.session.Parts.Work=foreign;return result
        h.t.native.object_inventory.side_effect=changed
        with self.assertRaisesRegex(RuntimeError,'рабочей и отображаемой'):h.p.run()
        h.collector.CopyCollector.assert_not_called();self.original_untouched(h)
        self.assertIs(h.t.session.Parts.Work,foreign);self.assertEqual(set(h.marks),{103})
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_existing_internal_collector_returned_as_clone_is_never_mutated(self):
        h=self.world();old=h.clone_collector();h.t.part.ScCollectors.remove(old)
        h.collector.CopyCollector.side_effect=lambda:old
        self.rejected(h,'единственную новую коллекцию')
        old.Destroy.assert_not_called();old.RemoveEvaluationFilter.assert_not_called()

    def test_any_preexisting_database_tag_prevents_treating_a_result_as_new(self):
        h=self.world();old=h.clone_collector();h.t.part.ScCollectors.remove(old)
        def inventory(*args):
            tags,containers=h.object_inventory(*args);return tags,containers-{old.Tag}
        h.t.native.object_inventory.side_effect=inventory
        h.collector.CopyCollector.side_effect=lambda:old
        self.rejected(h,'единственную новую коллекцию')
        old.Destroy.assert_not_called();old.RemoveEvaluationFilter.assert_not_called()

    def test_two_internal_clones_cannot_evade_delta_check(self):
        h=self.world();h.after_clone=lambda c:h.t.part.ScCollectors.remove(c)
        def duplicate():
            first=h.clone_collector();h.clone_collector();return first
        h.collector.CopyCollector.side_effect=duplicate
        self.rejected(h,'единственную новую коллекцию')
        for c in h.clones:c.Destroy.assert_not_called();c.RemoveEvaluationFilter.assert_not_called()

    def test_other_container_types_are_counted_without_instantiating_their_wrappers(self):
        h=self.world();other=NS(Tag=75001);h.w.register(other)
        def inventory(*args):
            tags,containers=h.object_inventory(*args);return tags,containers|{other.Tag}
        h.t.native.object_inventory.side_effect=inventory
        original=h.t.nx.TaggedObjectManager.GetTaggedObject
        def resolve(tag):
            if tag==other.Tag:raise AssertionError('Unrelated internal container wrapper must not be read')
            return original(tag)
        h.t.nx.TaggedObjectManager.GetTaggedObject=resolve
        before=h.snapshot()
        with self.assertRaises(M.HistoricalProbeComplete):h.p.run()
        self.assertEqual(h.snapshot(),before);self.original_untouched(h)

    def test_disappearing_existing_container_is_not_an_acceptable_copy_delta(self):
        h=self.world();old=h.clone_collector();h.t.part.ScCollectors.remove(old)
        h.after_clone=lambda c:h.w.objects.pop(old.Tag)
        self.rejected(h,'единственную новую коллекцию')
        h.clones[-1].Destroy.assert_not_called();h.clones[-1].RemoveEvaluationFilter.assert_not_called()

    def test_missing_public_clone_after_destroy_does_not_prove_database_deletion(self):
        h=self.world()
        h.after_clone=lambda c:setattr(c.Destroy,'side_effect',lambda:h.t.part.ScCollectors.remove(c))
        self.rejected(h,'состав ScCollectors')
        self.assertNotIn('временная коллекция удалена','\n'.join(h.t.lines))

    def test_partial_internal_clone_surviving_undo_invalidates_restoration(self):
        h=self.world();h.after_clone=lambda c:h.t.part.ScCollectors.remove(c)
        def failed():h.clone_collector();raise M.NativeApiError(7502,'CopyCollector','partial internal copy')
        h.collector.CopyCollector.side_effect=failed
        def damage():
            if h.events[-1]==('undo',103):h.w.register(h.clones[0])
        h.after_undo=damage
        with self.assertRaises(M.NativeApiError):h.p.run()
        self.assertIn('Не подтверждено удаление временных ScCollectors','\n'.join(h.t.lines))
        self.assertEqual(set(h.marks),{103});self.original_untouched(h)
        self.assertNotIn('Локальный Undo новых кривых выполнен','\n'.join(h.t.lines))

    def test_database_failure_after_destroy_is_not_reported_as_verified_cleanup(self):
        h=self.world();error=M.NativeApiError(7503,'UF_OBJ_ask_type_and_subtype','readback failed')
        h.after_clone_destroy=lambda c:setattr(h.t.native.object_inventory,'side_effect',error)
        def repaired():
            if h.events[-1]==('undo',103):h.t.native.object_inventory.side_effect=h.object_inventory
        h.after_undo=repaired
        self.assertIs(self.rejected(h,exception=M.NativeApiError),error)
        self.assertNotIn('временная коллекция удалена','\n'.join(h.t.lines))
        h.w.replace.UpdateMap.assert_not_called()


if __name__=='__main__':unittest.main()
