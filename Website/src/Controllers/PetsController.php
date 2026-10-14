<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Config;
use App\Core\Security;
use App\Core\View;
use App\Services\PetAdventureService;
use App\Services\PetArenaService;
use App\Services\PetGameService;
use App\Services\PetPlaygroundService;
use App\Services\PetService;
use App\Services\PetSkillService;

final class PetsController
{
    private function requireModule(): array
    {
        Auth::requirePermission('pets.use');
        if (!(bool)Config::get('pets.enabled', true)) {
            http_response_code(503);
            View::render('error', ['title'=>'Pets pausiert', 'message'=>'Das Pets-Modul ist momentan global deaktiviert.']);
            exit;
        }
        return Auth::user();
    }

    public function index(): void
    {
        $user = $this->requireModule();
        View::render('pets', ['title'=>'Meine Pets', 'currentPetPage'=>'overview'] + PetService::overview((int)$user['id']));
    }

    public function adventure(): void
    {
        $user = $this->requireModule();
        $data = $this->gameData((int)$user['id'], 'adventure');
        View::render('pet_adventure', ['title'=>'Pet-Abenteuer'] + $data + PetAdventureService::overview((int)$user['id']));
    }

    public function equipment(): void
    {
        $user = $this->requireModule();
        $data = $this->gameData((int)$user['id'], 'equipment');
        View::render('pet_equipment', ['title'=>'Pet-Ausrüstung'] + $data + PetGameService::equipmentOverview((int)$user['id']));
    }

    public function skills(): void
    {
        $user = $this->requireModule();
        $data = $this->gameData((int)$user['id'], 'skills');
        View::render('pet_skills', ['title'=>'Pet-Skillbaum'] + $data + PetSkillService::overview((int)$user['id']));
    }

    public function arena(): void
    {
        $user = $this->requireModule();
        $data = $this->gameData((int)$user['id'], 'arena');
        View::render('pet_arena', ['title'=>'Pet-Arena'] + $data + PetArenaService::overview((int)$user['id']));
    }

    public function playground(): void
    {
        $user = $this->requireModule();
        $data = $this->gameData((int)$user['id'], 'playground');
        View::render('pet_playground', ['title'=>'Pet-Spielwiese'] + $data + PetPlaygroundService::overview((int)$user['id']));
    }

    public function playgroundState(): void
    {
        $user = $this->requireModule();
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        try {
            $after=max(0,(int)($_GET['after']??0));
            echo json_encode(['ok'=>true] + PetPlaygroundService::state((int)$user['id'],$after), JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        } catch (\Throwable $error) {
            error_log('Pet-Spielwiese Statusfehler: '.$error->getMessage());
            http_response_code(500);
            echo json_encode(['ok'=>false,'error'=>'Die Spielwiese konnte gerade nicht synchronisiert werden.'], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
        }
        exit;
    }

    public function adopt(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $petId = PetService::adopt((int)$user['id'], (string)($_POST['species'] ?? ''), (string)($_POST['name'] ?? ''));
            Audit::log('pets.adopted', 'pet', (string)$petId);
            View::flash('success', 'Dein Pet ist eingezogen. Viel Freude miteinander!');
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets');
    }

    public function action(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetService::performAction((int)$user['id'], (string)($_POST['action'] ?? ''), (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log('pets.action.' . $result['action'], 'pet', (string)$result['pet_id']);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets');
    }

    public function claim(string $id): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $task = PetService::claimTask((int)$user['id'], (int)$id);
            Audit::log('pets.task.claimed', 'pet_task', (string)$id, ['coins'=>$task['reward_coins'], 'xp'=>$task['reward_xp']]);
            View::flash('success', 'Aufgabe eingelöst: +' . $task['reward_coins'] . ' Münzen und +' . $task['reward_xp'] . ' XP.');
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets#tasks');
    }

    public function startAdventure(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetAdventureService::start(
                (int)$user['id'],
                (string)($_POST['adventure_key'] ?? ''),
                (string)($_POST['tactic'] ?? ''),
                (string)($_POST['idempotency_key'] ?? '')
            );
            if (!$result['duplicate']) Audit::log('pets.adventure.started', 'pet_adventure', (string)$result['adventure_id'], ['adventure'=>$result['adventure_key'], 'tactic'=>$result['tactic']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/adventure');
    }

    public function claimAdventure(string $id): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetAdventureService::claim((int)$user['id'], (int)$id, (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log('pets.adventure.claimed', 'pet_adventure', (string)$result['adventure_id'], ['coins'=>$result['coins'], 'xp'=>$result['xp'], 'item'=>$result['item_name']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/adventure');
    }

    public function equipItem(string $id): void
    {
        $this->changeEquipment($id, true);
    }

    public function unequipItem(string $id): void
    {
        $this->changeEquipment($id, false);
    }

    public function salvageItem(string $id): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetGameService::salvageItem((int)$user['id'], (int)$id, (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log('pets.item.salvaged', 'pet_item', (string)$result['item_id'], ['coins'=>$result['coins']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/equipment');
    }

    public function unlockSkill(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetSkillService::unlock((int)$user['id'], (string)($_POST['skill_key'] ?? ''), (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log('pets.skill.unlocked', 'pet', (string)$result['pet_id'], ['skill'=>$result['skill_key'], 'rank'=>$result['rank'], 'cost'=>$result['cost']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/skills');
    }

    public function resetSkills(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetSkillService::reset((int)$user['id'], (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log('pets.skills.reset', 'pet', (string)$result['pet_id'], ['refunded'=>$result['refunded'], 'coins'=>$result['cost']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/skills');
    }

    public function fightArena(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = PetArenaService::fight(
                (int)$user['id'],
                (string)($_POST['opponent_key'] ?? ''),
                (string)($_POST['tactic'] ?? ''),
                (string)($_POST['idempotency_key'] ?? '')
            );
            if (!$result['duplicate']) Audit::log('pets.arena.fought', 'pet_arena_battle', (string)$result['battle_id'], ['opponent'=>$result['opponent_key'], 'result'=>$result['result'], 'rating_change'=>$result['rating_change']]);
            View::flash($result['result']==='win'?'success':'info', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/arena');
    }

    public function playTogether(): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        $location='/pets/playground';
        try {
            $result=PetPlaygroundService::play(
                (int)$user['id'],
                (int)($_POST['target_pet_id']??0),
                (string)($_POST['interaction_type']??''),
                (string)($_POST['idempotency_key']??'')
            );
            if(!$result['duplicate'])Audit::log('pets.playground.played','pet_playground_interaction',(string)$result['interaction_id'],['target_pet_id'=>$result['target_pet_id'],'interaction'=>$result['interaction_type']]);
            View::flash('success',$result['message']);
            $location.='?moment='.(int)$result['interaction_id'];
        } catch (\Throwable $error) { $this->flashFailure($error); }
        header('Location: '.$location);
    }

    private function gameData(int $userId, string $page): array
    {
        $data = PetGameService::commonOverview($userId);
        if (!$data['pet']) {
            View::flash('info', 'Adoptiere zuerst ein Pet, bevor du diesen Bereich betrittst.');
            header('Location: /pets');
            exit;
        }
        $data['currentPetPage'] = $page;
        return $data;
    }

    private function changeEquipment(string $id, bool $equip): void
    {
        $user = $this->requireModule();
        Security::verifyCsrf();
        try {
            $result = $equip
                ? PetGameService::equipItem((int)$user['id'], (int)$id, (string)($_POST['idempotency_key'] ?? ''))
                : PetGameService::unequipItem((int)$user['id'], (int)$id, (string)($_POST['idempotency_key'] ?? ''));
            if (!$result['duplicate']) Audit::log($equip?'pets.item.equipped':'pets.item.unequipped', 'pet_item', (string)$result['item_id'], ['slot'=>$result['slot']]);
            View::flash('success', $result['message']);
        } catch (\Throwable $e) { $this->flashFailure($e); }
        header('Location: /pets/equipment');
    }

    private function flashFailure(\Throwable $error): void
    {
        if ($error instanceof \PDOException) {
            error_log('Pet-Modul Datenbankfehler: ' . $error->getMessage());
            View::flash('error', 'Die Pet-Aktion konnte wegen eines Datenbankfehlers nicht abgeschlossen werden. Bitte versuche es erneut.');
            return;
        }
        if (!($error instanceof \RuntimeException) && !($error instanceof \InvalidArgumentException)) {
            error_log('Pet-Modul interner Fehler (' . $error::class . '): ' . $error->getMessage());
            View::flash('error', 'Die Pet-Aktion konnte wegen eines internen Fehlers nicht abgeschlossen werden. Bitte versuche es erneut.');
            return;
        }
        View::flash('error', $error->getMessage());
    }
}
