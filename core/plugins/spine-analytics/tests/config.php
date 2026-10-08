<?php
declare(strict_types=1);
require dirname(__DIR__).'/ReportAccess.php';
require dirname(__DIR__).'/PasswordConfig.php';
use Webspine\Providers\SpineAnalytics\{PasswordConfig,ReportAccess};
function configCheck(bool $ok,string $label):void {if(!$ok)throw new RuntimeException($label);echo "PASS $label\n";}
$root=sys_get_temp_dir().'/spine-config-'.bin2hex(random_bytes(6));mkdir($root,0700);
$path=$root.'/local.php';
try {
    foreach ([false,true] as $existingHash) {
        $password="local-test-quote'backslash\\tail\\'end";
        $section="'password' => ".var_export($password,true).", 'enabled' => false".($existingHash ? ", 'password_hash' => null" : '');
        $before="<?php\n// Preserve comments and executable unrelated settings.\nreturn [\n 'smtp' => ['password' => getenv('UNRELATED_TEST_SECRET') ?: 'untouched'],\n 'spine_analytics' => [$section],\n 'plugins' => ['spine-analytics'],\n];\n";
        file_put_contents($path,$before);
        configCheck(PasswordConfig::hashFile($path),'Convert plaintext with '.($existingHash?'existing':'absent').' hash key');
        $after=file_get_contents($path);$config=require $path;
        configCheck($config['spine_analytics']['password']===null && password_verify($password,$config['spine_analytics']['password_hash']),'Plaintext removed and exact password including escapes verifies');
        configCheck(str_contains($after,"// Preserve comments and executable unrelated settings.") && str_contains($after,"'smtp' => ['password' => getenv('UNRELATED_TEST_SECRET') ?: 'untouched']") && $config['plugins']===['spine-analytics'],'Unrelated code, comments and settings preserved');
        configCheck(!PasswordConfig::hashFile($path) && file_get_contents($path)===$after,'Repeat conversion is idempotent');
        $newPassword=bin2hex(random_bytes(16));$rotation=str_replace("'password' => null","'password' => ".var_export($newPassword,true),$after);file_put_contents($path,$rotation);
        PasswordConfig::hashFile($path);$rotated=require $path;$access=new ReportAccess($rotated['spine_analytics']['password_hash']);
        configCheck($access->status('Basic '.base64_encode('analytics:'.$newPassword),true)===200 && $access->status('Basic '.base64_encode('analytics:'.$password),true)===401,'Configuration conversion rotates access');
    }
    foreach ([
        "<?php return ['spine_analytics'=>['password'=>'short']];",
        "<?php return ['spine_analytics'=>['password'=>getenv('PASSWORD')]];",
        "<?php return ['spine_analytics'=>['password'=>'1234567890123456','password'=>'abcdefghijklmnop']];",
        "<?php return ['spine_analytics'=>['password'=>'1234567890123456', ...[]]];",
        "<?php return ['spine_analytics'=>['password'=>'1234567890123456','password_hash'=>getenv('HASH')]];",
        "<?php return ['spine_analytics'=>['password'=>'1234567890123456']],",
    ] as $invalid) {
        file_put_contents($path,$invalid);$failed=false;
        try {PasswordConfig::hashFile($path);}catch(Throwable $error){$failed=true;configCheck(!str_contains($error->getMessage(),'1234567890123456'),'Conversion errors do not echo secrets');}
        configCheck($failed && file_get_contents($path)===$invalid,'Invalid or ambiguous config remains unchanged');
    }
    configCheck(count(glob($path.'.tmp-*'))===0,'No temporary configuration files left behind');
} finally {if(is_file($path))unlink($path);rmdir($root);}
