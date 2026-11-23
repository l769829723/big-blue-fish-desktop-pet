#!/usr/bin/env python3
"""Run against an actual published Linux app under an isolated X11 desktop."""
import base64, json, os, pathlib, queue, subprocess, sys, threading, time, xml.etree.ElementTree as ET

payload = pathlib.Path(sys.argv[1]).resolve()
data = pathlib.Path(sys.argv[2]).resolve()
data.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, BIGBLUEFISH_DATA=str(data), XDG_SESSION_TYPE="x11")
count = 0
def check(ok, label):
    global count
    assert ok, label
    count += 1
    print(f"PASS {count}: {label}", flush=True)
def wait(condition, seconds=12):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        try:
            result = condition()
            if result: return result
        except (FileNotFoundError, json.JSONDecodeError): pass
        time.sleep(.08)
    raise TimeoutError(str(condition))
def state(): return json.loads((data / 'test-status.json').read_text())
def command(value):
    temporary=data/'test-command.txt.tmp';temporary.write_text(value);temporary.replace(data/'test-command.txt')
for name in ['test-status.json','test-error.txt','test-command.txt','speech-pacing.json']:
    (data / name).unlink(missing_ok=True)
# McpEnabled is a computed alias of AgentEnabled and is not persisted, so the standard
# run has to enable the Agent connection explicitly; the Safe edition forces it off.
initial_preferences = dict(ModelEnabled=False,PublicInfo=False,CheckUpdatesOnStartup=False,ExternalMcpEnabled=True,RoamingEnabled=False)
if '--safe' not in sys.argv: initial_preferences.update(AgentEnabled=True,PuppetMode=True)
(data / 'preferences.json').write_text(json.dumps(initial_preferences))
log = (data / 'app.log').open('w')
app = subprocess.Popen([str(payload / 'BigBlueFish.Linux'), '--test-mode'], env=env, stdout=log, stderr=log)
bridge = fixture = None
roam_fixtures = []
try:
    wait(lambda: state().get('frames',0) > 10)
    check(app.poll() is None, 'actual self-contained Linux window running')
    # A line must finish typing before the app asks the model for the next one, even
    # when the speech interval elapsed before the reply became visible.
    command('speech-pacing')
    pacing = wait(lambda: json.loads((data / 'speech-pacing.json').read_text()), 30)
    check(pacing['checks'] >= 6, 'real bubble finishes typing before requesting the next line')
    check(not (data / 'test-error.txt').exists(), 'speech pacing regression runs without application errors')
    if '--safe' in sys.argv:
        check(not state()['McpAvailable'] and not state()['Puppet'], 'safe keeps the Agent connection and puppet off')
        check((payload/'BigBlueFish.Mcp').exists() and (payload/'BigBlueFish.Control.dll').exists(), 'safe package bundles the restricted MCP bridge and control layer')
        check('BigBlueFish.Control' in (payload/'BigBlueFish.Linux.deps.json').read_text(), 'safe executable links the shared control layer')
        safe_pipe = wait(lambda: state().get('mcpPipe'), 20)
        check(safe_pipe, 'safe serves the stable external pipe when the switch is on')
        bridge = subprocess.Popen([str(payload / 'BigBlueFish.Mcp')], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
        safe_lines = queue.Queue()
        threading.Thread(target=lambda: [safe_lines.put(json.loads(line)) for line in bridge.stdout], daemon=True).start()
        safe_id = [0]
        def safe_ask(method, params={}):
            safe_id[0] += 1; number = safe_id[0]
            bridge.stdin.write(json.dumps(dict(jsonrpc='2.0', id=number, method=method, params=params), ensure_ascii=False) + '\n'); bridge.stdin.flush()
            while True:
                value = safe_lines.get(timeout=30)
                if value.get('id') == number: return value
        safe_init = safe_ask('initialize', dict(protocolVersion='2025-11-25', capabilities={}, clientInfo=dict(name='safe external verification', version='1')))['result']
        check('外部 MCP 接口' in safe_init['instructions'] and not safe_init['capabilities']['resources']['subscribe'], 'safe handshake offers speech and actions only')
        bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); bridge.stdin.flush()
        safe_names = [item['name'] for item in safe_ask('tools/list')['result']['tools']]
        check(safe_names == ['list_actions', 'play_action', 'interrupt_action', 'say', 'say_next'], 'safe external pipe exposes exactly five tools')
        safe_text = '无ACP版的外部原文 + 动作'
        safe_ask('tools/call', dict(name='say', arguments=dict(text=safe_text)))
        wait(lambda: state()['text'] == safe_text, 5)
        check(True, 'safe external say reaches the real Linux bubble')
        safe_ask('tools/call', dict(name='interrupt_action', arguments=dict(action='点击回应-元气挥手')))
        wait(lambda: state()['currentAction'] == '点击回应-元气挥手', 5)
        check(True, 'safe external interrupt_action plays the requested clip')
        denied = safe_ask('tools/call', dict(name='update_settings', arguments=dict(settings=dict(Scale=1.5))))
        check(denied['result'].get('isError') is True, 'safe external client cannot change settings')
        denied = safe_ask('tools/call', dict(name='capture_screen', arguments=dict()))
        check(denied['result'].get('isError') is True, 'safe external client cannot capture the screen')
        check(safe_ask('resources/list')['result']['resources'] == [], 'safe external client sees no interaction resources')
        bridge.stdin.close(); bridge.wait(timeout=5)
        command('settings'); wait(lambda: state()['settingsOpen'])
        command('render-settings'); wait(lambda: (data/'settings.png').exists())
        command('close-settings'); wait(lambda: not state()['settingsOpen'])
        check(state()['phase'] != 'PuppetIdle', 'safe continues ordinary animation selection')
        check(not (data/'test-error.txt').exists(),'no safe application errors')
        print(f'Linux Safe actual integration: {count} checks passed.', flush=True)
        sys.exit(0)
    check(state()['Puppet'] and state()['currentAction']=='待机呼吸休闲', 'startup puppet uses fixed breathing idle')
    # The internal service listens on a per-conversation pipe; test mode publishes it.
    pipe = wait(lambda: state().get('mcpPipe'), 20)
    bridge = subprocess.Popen([str(payload / 'BigBlueFish.Mcp'), '--pipe', pipe], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    lines = queue.Queue(); events=[]; request_id=0
    def receive():
        for line in bridge.stdout: lines.put(json.loads(line))
    threading.Thread(target=receive, daemon=True).start()
    def request(method, params={}):
        global request_id
        request_id+=1; number=request_id
        bridge.stdin.write(json.dumps(dict(jsonrpc='2.0',id=number,method=method,params=params),ensure_ascii=False)+'\n'); bridge.stdin.flush()
        while True:
            value=lines.get(timeout=30)
            if value.get('id')==number:
                assert 'result' in value,value
                return value['result']
            events.append(value)
    def tool(name, **arguments):
        result=request('tools/call',dict(name=name,arguments=arguments)); assert not result.get('isError'),result; return result
    def content(name, **arguments): return json.loads(tool(name,**arguments)['content'][0]['text'])
    init=request('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='Linux verification',version='1')))
    bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); bridge.stdin.flush()
    version=ET.parse(pathlib.Path(__file__).resolve().parents[3]/'Version.props').findtext('.//Version')
    check(init['serverInfo']['version']==version,'real stdio bridge negotiates current version')
    check(len(request('tools/list')['tools'])==14,'all internal MCP tools available')
    check(len(content('list_actions'))==106,'same 106 production actions')
    text='主人，Linux 端收到的原文呀~ ABC 123'
    content('say',text=text); wait(lambda: state()['text']==text)
    check(state()['modelPid'] is None and state()['bubbleVisible'],'literal speech works without local model')
    content('interrupt_action',action='点击回应-元气挥手')
    content('play_action',action='吃白饭')
    wait(lambda: state()['currentAction']=='点击回应-元气挥手',3)
    wait(lambda: state()['currentAction']=='吃白饭',14)
    check(True,'queued animation begins after actual decoder completion')
    wait(lambda: state()['phase']=='PuppetIdle',14)
    check(True,'external action returns to fixed idle')
    # A second bridge with no --pipe uses the stable external pipe: speech and actions only.
    external = subprocess.Popen([str(payload / 'BigBlueFish.Mcp')], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    try:
        external_lines = queue.Queue()
        threading.Thread(target=lambda: [external_lines.put(json.loads(line)) for line in external.stdout], daemon=True).start()
        counter = [0]
        def ask(method, params={}):
            counter[0] += 1; number = counter[0]
            external.stdin.write(json.dumps(dict(jsonrpc='2.0', id=number, method=method, params=params), ensure_ascii=False) + '\n'); external.stdin.flush()
            while True:
                value = external_lines.get(timeout=30)
                if value.get('id') == number: return value
        handshake = ask('initialize', dict(protocolVersion='2025-11-25', capabilities={}, clientInfo=dict(name='external verification', version='1')))['result']
        check('外部 MCP 接口' in handshake['instructions'] and not handshake['capabilities']['resources']['subscribe'], 'external handshake offers speech and actions only')
        external.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); external.stdin.flush()
        names = [tool['name'] for tool in ask('tools/list')['result']['tools']]
        check(names == ['list_actions', 'play_action', 'interrupt_action', 'say', 'say_next'], 'external pipe exposes exactly five tools')
        literal = '外部 MCP 的原文 + 动作'
        ask('tools/call', dict(name='say', arguments=dict(text=literal)))
        wait(lambda: state()['text'] == literal, 5)
        check(True, 'external say reaches the real Linux bubble')
        ask('tools/call', dict(name='interrupt_action', arguments=dict(action='点击回应-元气挥手')))
        wait(lambda: state()['currentAction'] == '点击回应-元气挥手', 5)
        check(True, 'external interrupt_action plays the requested clip')
        denied = ask('tools/call', dict(name='update_settings', arguments=dict(settings=dict(Scale=1.5))))
        check(denied['result'].get('isError') is True, 'external client cannot change settings')
        denied = ask('tools/call', dict(name='capture_screen', arguments=dict()))
        check(denied['result'].get('isError') is True, 'external client cannot capture the screen')
        check('error' in ask('resources/read', dict(uri='bigbluefish://interactions')), 'external client cannot read interaction history')
        external.stdin.close(); check(external.wait(timeout=5) == 0, 'external bridge exits when its client closes stdin')
    finally:
        if external.poll() is None: external.terminate(); external.wait(timeout=5)
        external = None
    content('say_next',text='下次自动节奏')
    command('speech-test'); time.sleep(2)
    check(state()['text']==literal and state()['modelPid'] is None,'puppet rejects speech test and does not consume scheduled speech')
    old=state(); x,y=old['X']+250,old['Y']+130
    subprocess.run(['xdotool','mousemove',str(x),str(y),'click','1'],check=True,env=env)
    # Wait past the 500 ms double-click window so the drag is not taken as a double click.
    time.sleep(1.0)
    subprocess.run(['xdotool','mousemove',str(x),str(y),'mousedown','1','mousemove',str(x-40),str(y-20),'sleep','.2','mouseup','1'],check=True,env=env)
    time.sleep(1)
    content('get_settings')
    kinds=[e.get('params',{}).get('data',{}).get('kind') for e in events]
    check(all(k in kinds for k in ['click','drag_start','drag_end']),'real pointer click and drag pushed automatically')
    check(state()['currentAction']=='待机呼吸休闲','pointer gestures do not switch puppet animation')
    content('update_settings',settings={'McpChatEnabled':True})
    location=state(); x,y=location['X']+250,location['Y']+130
    subprocess.run(['xdotool','mousemove',str(x),str(y),'click','--repeat','2','--delay','120','1'],check=True,env=env)
    wait(lambda:content('get_settings')['state']['inputOpen'],5)
    def input_window():
        # Find the real top-level input by process and size; xdotool's title regex depends on locale for CJK text.
        search=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True,capture_output=True)
        for window in search.stdout.splitlines():
            geometry=subprocess.run(['xdotool','getwindowgeometry','--shell',window],env=env,text=True,capture_output=True)
            values=dict(line.split('=',1) for line in geometry.stdout.splitlines() if '=' in line)
            if 300<=int(values.get('WIDTH',0))<=354 and 100<=int(values.get('HEIGHT',0))<=120:return window
        return None
    entry=wait(input_window,5)
    check(bool(entry),'actual double-click opens input only after enabling connected MCP chat')
    command('render-input'); wait(lambda:(data/'input.png').exists())
    (data/'input-empty.png').write_bytes((data/'input.png').read_bytes())
    geometry=subprocess.check_output(['xdotool','getwindowgeometry','--shell',entry],env=env,text=True)
    dimensions=dict(line.split('=',1) for line in geometry.splitlines() if '=' in line)
    check(int(dimensions['WIDTH'])<=354 and int(dimensions['HEIGHT'])<=120,'input is a compact pet attachment')
    content('update_settings',settings={'Left':500,'Top':300})
    time.sleep(.5)
    moved_geometry=subprocess.check_output(['xdotool','getwindowgeometry','--shell',entry],env=env,text=True)
    check(moved_geometry!=geometry,'attached input follows MCP pet movement')
    command('hide'); wait(lambda:state()['hidden'])
    def input_hidden():
        result=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True,capture_output=True)
        return entry not in result.stdout.splitlines()
    check(wait(input_hidden,3),'hiding pet hides its attached editor')
    command('show'); wait(lambda:state()['visible'] and not state()['hidden'])
    wait(lambda:input_window()==entry,3)
    check(True,'restoring pet restores the same attached editor')
    subprocess.run(['xdotool','windowactivate','--sync',entry],check=True,env=env)
    message='Hello fish from Linux 123 & <>'
    subprocess.run(['xdotool','type','--clearmodifiers','--delay','3',message],check=True,env=env)
    (data/'input.png').unlink(); command('render-input');wait(lambda:(data/'input.png').exists())
    subprocess.run(['xdotool','key','ctrl+Return'],check=True,env=env);time.sleep(.4)
    content('get_settings')
    submitted=[e['params']['data']['detail']['text'] for e in events if e.get('params',{}).get('data',{}).get('kind')=='user_message']
    if message not in submitted:print('Submitted test messages:',repr(submitted),flush=True)
    check(message in submitted,'actual keyboard input travels over MCP unchanged')
    subprocess.run(['xdotool','key','alt+F4'],check=True,env=env)
    # Foreground fixture is a separate SDL application, not a title injected into the fish.
    fixture=subprocess.Popen(['/usr/bin/ffplay','-loglevel','error','-window_title','BigBlueFish Development','-f','lavfi','-i',"color=c=white:s=800x500:r=1,drawtext=text='Developing BigBlueFish Project':fontcolor=black:fontsize=36:x=40:y=100"],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    time.sleep(2)
    found=subprocess.check_output(['xdotool','search','--name','^BigBlueFish Development$'],env=env,text=True).splitlines()[0]
    subprocess.run(['xdotool','windowactivate','--sync',found],check=True,env=env); time.sleep(.5)
    for name in ['capture_foreground','capture_screen']:
        result=tool(name,max_dimension=1280); image=base64.b64decode(result['content'][1]['data']); metadata=json.loads(result['content'][0]['text'])
        check(result['content'][1]['mimeType']=='image/png' and image[:8]==b'\x89PNG\r\n\x1a\n',name+' actual PNG')
        (data / (name+'.png')).write_bytes(image)
        if name=='capture_foreground': check('BigBlueFish Development' in metadata['identity'],'foreground selected from real external SDL window')
    command('ocr'); wait(lambda: (data/'ocr-status.json').exists(),30)
    ocr=json.loads((data/'ocr-status.json').read_text())
    check('BigBlueFish' in ocr['Context'] and ocr['WordCount']>2,'actual tesseract OCR reads foreground pixels')
    command('render'); wait(lambda: (data/'pet.png').exists())
    command('settings'); wait(lambda: state()['settingsOpen'])
    command('render-settings'); wait(lambda: (data/'settings.png').exists())
    settings_windows=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines()
    subprocess.run(['xdotool','windowactivate','--sync',found],check=True,env=env)
    content('update_settings',settings={'BubbleScale':1.35,'AnimationIntervalSeconds':65})
    time.sleep(.4)
    after_windows=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines()
    active=subprocess.check_output(['xdotool','getactivewindow'],env=env,text=True).strip()
    check(set(settings_windows)==set(after_windows) and active==found and state()['settingsOpen'], 'MCP refresh keeps the same Linux settings window without taking foreground')
    command('close-settings'); wait(lambda: not state()['settingsOpen'])
    content('update_settings',settings={'PuppetMode':False,'BubbleScale':1.2})
    wait(lambda:not state()['Puppet'])
    check(json.loads((data/'preferences.json').read_text())['BubbleScale']==1.2,'settings saved and puppet disabled explicitly')
    # Roaming: climb a real X11 window, follow it, fall when it vanishes, and push
    # it without ever moving it. Enabled only here so earlier checks stay still.
    def geometry(handle):
        # xdotool reports frame-adjusted values on reparenting window managers, so
        # read the client's absolute rectangle straight from xwininfo.
        text=subprocess.check_output(['xwininfo','-id',str(handle)],env=env,text=True)
        def field(name):
            for line in text.splitlines():
                if line.strip().startswith(name+':') and ':' in line:
                    return int(line.split(':',1)[1].strip())
            raise ValueError(name)
        return dict(X=field('Absolute upper-left X'),Y=field('Absolute upper-left Y'),
            WIDTH=field('Width'),HEIGHT=field('Height'))
    def frame_top(handle):
        output=subprocess.check_output(['xprop','-id',handle,'_NET_FRAME_EXTENTS'],env=env,text=True)
        if '=' in output:
            values=[int(value) for value in output.split('=')[1].split(',') if value.strip().isdigit()]
            if len(values)==4: return values[2]
        return 0
    def spawn_roam_fixture(title,left,top,width=360,height=300):
        process=subprocess.Popen(['/usr/bin/ffplay','-loglevel','error','-window_title',title,'-f','lavfi','-i',
            f"color=c=white:s={width}x{height}:r=1,drawtext=text='Roam Target':fontcolor=black:fontsize=28:x=24:y=40",
            '-left',str(left),'-top',str(top)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        roam_fixtures.append(process)
        time.sleep(2)
        handle=subprocess.check_output(['xdotool','search','--name','^'+title+'$'],env=env,text=True).splitlines()[0]
        subprocess.run(['xdotool','windowmove',handle,str(left),str(top)],check=True,env=env);time.sleep(.6)
        return handle
    content('update_settings',settings={'RoamingEnabled':True,'Left':410,'Top':440})
    wait(lambda:abs(state()['X']-410)<3 and abs(state()['Y']-440)<3)
    perch_window=spawn_roam_fixture('BigBlueFish Roam',600,380)
    command('roam-perch:'+perch_window)
    perch=wait(lambda:(lambda s: s if s['roamState']=='Perch' else None)(state()),90)
    check(perch['roamTarget']=='BigBlueFish Roam','real X11 window is recognised as a perch target')
    start_geometry=geometry(perch_window)
    check(abs(perch['roamFeetY']-(start_geometry['Y']-frame_top(perch_window)+2))<3,'feet rest on the real window top edge')
    check(start_geometry['X']-1<=perch['roamFeetX']<=start_geometry['X']+start_geometry['WIDTH']+1,'perch stays within the window width')
    subprocess.run(['xdotool','windowmove',perch_window,'680','330'],check=True,env=env);time.sleep(.4)
    moved_geometry=geometry(perch_window)
    dx=moved_geometry['X']-start_geometry['X']
    followed=wait(lambda:(lambda s: s if s['roamState']=='Perch' and abs(s['roamFeetY']-(moved_geometry['Y']-frame_top(perch_window)+2))<3 else None)(state()),10)
    check(abs(dx)<1 or abs((followed['roamFeetX']-perch['roamFeetX'])-dx)<6,'perched pet follows the real window instead of staying behind')
    check(followed['roamTarget']=='BigBlueFish Roam','perch keeps its target snapshot current')
    roam_fixtures[0].terminate();roam_fixtures[0].wait(timeout=5)
    landed=wait(lambda:(lambda s: s if s['roamState']=='Idle' and s['roamTarget'] is None and s['roamFeetY']>perch['roamFeetY']+100 else None)(state()),20)
    check(True,'a vanishing window drops the pet back to the floor')
    floor=landed['roamFeetY']
    content('update_settings',settings={'Left':410,'Top':int(floor-261)})
    wait(lambda:abs(state()['X']-410)<3)
    push_window=spawn_roam_fixture('BigBlueFish Push',600,int(floor-300))
    # The pet never pushes the window the user is working in, so hand focus to a
    # small window that is neither a perch nor a push target before asking for one.
    focus_window=spawn_roam_fixture('BigBlueFish Focus',120,40,width=200,height=120)
    subprocess.run(['xdotool','windowactivate',focus_window],check=True,env=env);time.sleep(.6)
    check(subprocess.check_output(['xdotool','getactivewindow'],env=env,text=True).strip()==str(int(focus_window)),
        'a non-target window holds focus before the cosmetic push')
    command('roam-push:'+push_window)
    wait(lambda:state()['roamState']=='Push',90)
    push_before=geometry(push_window)
    wait(lambda:state()['roamState']=='Idle',60)
    check(geometry(push_window)==push_before,'cosmetic push never moves the target window')
    content('update_settings',settings={'RoamingEnabled':False})
    check(not (data/'test-error.txt').exists(),'roaming produced no application error')
    # Keep the physical drag target outside the centered consent window and fixture.
    # An obscured point belongs to the window covering it, not the pet beneath it.
    content('update_settings',settings={'Left':900,'Top':450})
    wait(lambda:abs(state()['X']-900)<2 and abs(state()['Y']-450)<2)
    prior_windows=set(subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines())
    request_id+=1; consent_id=request_id
    bridge.stdin.write(json.dumps(dict(jsonrpc='2.0',id=consent_id,method='tools/call',params=dict(name='update_settings',arguments=dict(settings={'PuppetMode':True}))))+'\n');bridge.stdin.flush()
    def consent_window():
        windows=set(subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines())
        extra=windows-prior_windows
        return next(iter(extra)) if extra else None
    notice=wait(consent_window,6); before_drag=state(); x,y=before_drag['X']+250,before_drag['Y']+130
    capture=tool('capture_screen',max_dimension=1280)
    (data/'pending-consent.png').write_bytes(base64.b64decode(capture['content'][1]['data']))
    print('Pending consent pointer target:',x,y,'action:',before_drag['currentAction'],flush=True)
    subprocess.run(['xdotool','mousemove',str(x),str(y),'mousedown','1','mousemove',str(x-40),str(y-20),'sleep','.2','mouseup','1'],check=True,env=env)
    # Status is written once a second; wait for the moved window to appear in it.
    def moved_during_consent():
        current=state()
        return current if (current['X'],current['Y'])!=(before_drag['X'],before_drag['Y']) else None
    after_drag=wait(moved_during_consent,3)
    check((after_drag['X'],after_drag['Y'])!=(before_drag['X'],before_drag['Y']) and not after_drag['Puppet'],'real Linux pet remains draggable while mode consent is pending')
    subprocess.run(['xdotool','windowactivate','--sync',notice,'key','alt+F4'],check=True,env=env)
    def cancelled_response():
        request('ping')
        return next((e for e in events if e.get('id')==consent_id),None)
    cancelled=wait(cancelled_response,5)
    check(cancelled.get('result',{}).get('isError') and not state()['Puppet'],'closing pending notice cancels mode change without disabling pet')
    content('update_settings',settings={'Left':420,'Top':220})
    wait(lambda:abs(state()['X']-420)<2 and abs(state()['Y']-220)<2)
    check(True,'MCP coordinate settings move the actual Linux window')
    check(not (data/'test-error.txt').exists(),'no application diagnostic errors')
finally:
    if bridge: bridge.terminate(); bridge.wait(timeout=5)
    if fixture: fixture.terminate(); fixture.wait(timeout=5)
    for roam_fixture in roam_fixtures:
        if roam_fixture.poll() is None:
            roam_fixture.terminate()
            try: roam_fixture.wait(timeout=5)
            except subprocess.TimeoutExpired: roam_fixture.kill()
    if app.poll() is None:
        command('exit')
        try: app.wait(timeout=5)
        except subprocess.TimeoutExpired: app.kill(); app.wait()
    log.close()
if '--safe' not in sys.argv:
    saved=json.loads((data/'preferences.json').read_text())
    check(saved['Left']==420 and saved['Top']==220,'actual Linux position persists after native window shutdown')
    print(f'Linux actual integration: {count} checks passed.',flush=True)
