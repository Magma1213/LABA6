"""Мини-эмулятор CLI Cisco IOS (маршрутизатор 1841) для команд лабораторной работы №3."""

IF_FULL = {'fa0/0': 'FastEthernet0/0', 'fa0/1': 'FastEthernet0/1'}

def norm_if(s):
    s = s.lower().replace('fastethernet', 'fa')
    return s if s in IF_FULL else None

class Net:
    def __init__(self):
        self.routers = []

class Router:
    def __init__(self, net, mac):
        self.net = net; net.routers.append(self)
        self.host = 'Router'; self.mode = 'user'; self.cur_if = None
        self.enable_pw = None; self.history = []; self.mac = mac
        self.ifs = {k: {'shut': True, 'desc': None} for k in IF_FULL}
        self.saved = False

    # --- вспомогательное
    def prompt(self):
        suf = {'user': '>', 'priv': '#', 'config': '(config)#', 'if': '(config-if)#'}[self.mode]
        return self.host + suf

    def peer(self):
        o = [r for r in self.net.routers if r is not self]
        return o[0] if o else None

    def up(self, k):
        if k != 'fa0/0' or self.ifs[k]['shut']:
            return False
        p = self.peer()
        return bool(p and not p.ifs['fa0/0']['shut'])

    def state(self, k):
        i = self.ifs[k]
        if i['shut']:
            return 'administratively down', 'down'
        return ('up', 'up') if self.up(k) else ('down', 'down')

    # --- выполнение одной строки; возвращает список строк вывода
    def run(self, line):
        line = line.strip()
        if line:
            self.history.append(line)
        if not line:
            return []
        w = line.split()
        c = w[0].lower()
        m = self.mode
        if c == '?' or line.endswith('?'):
            return self.help(line)
        if c in ('en', 'ena', 'enable') and m == 'user':
            self.mode = 'priv'; return []
        if c in ('disable',) and m == 'priv':
            self.mode = 'user'; return []
        if c == 'exit':
            self.mode = {'priv': 'user', 'user': 'user', 'config': 'priv', 'if': 'config'}[m]; return []
        if c == 'end' and m in ('config', 'if'):
            self.mode = 'priv'; return []
        if c in ('conf', 'config', 'configure') and m == 'priv':
            return ['Enter configuration commands, one per line.  End with CNTL/Z.'] \
                if self.set_mode('config') else []
        if m == 'config':
            if c == 'hostname' and len(w) > 1:
                self.host = w[1]; return []
            if c == 'enable' and len(w) > 2 and w[1] == 'password':
                self.enable_pw = w[2]; return []
            if c == 'interface' or c == 'int':
                k = norm_if(w[1]) if len(w) > 1 else None
                if k:
                    self.cur_if = k; self.mode = 'if'; return []
        if m == 'if':
            k = self.cur_if
            if c == 'interface' or c == 'int':
                k2 = norm_if(w[1]) if len(w) > 1 else None
                if k2:
                    self.cur_if = k2; return []
            if c == 'no' and len(w) > 1 and w[1].startswith('sh'):
                return self.set_shut(k, False)
            if c.startswith('shut'):
                return self.set_shut(k, True)
            if c == 'description':
                self.ifs[k]['desc'] = line.split(None, 1)[1]; return []
            if c == 'clock':
                return self.invalid(line, 'clock')
        if m in ('user', 'priv') and c == 'show':
            return self.show(w[1:] if len(w) > 1 else [])
        if m == 'priv' and c in ('write', 'wr') :
            self.saved = True
            return ['Building configuration...', '[OK]']
        if m == 'priv' and c == 'copy' and len(w) >= 3:
            self.saved = True
            return ['Destination filename [startup-config]? ', 'Building configuration...', '[OK]']
        return self.invalid(line, c)

    def set_mode(self, m):
        self.mode = m; return True

    def invalid(self, line, first):
        return [' ' * len(self.prompt()) + '^', "% Invalid input detected at '^' marker.", '']

    def set_shut(self, k, shut):
        self.ifs[k]['shut'] = shut
        out = []
        if not shut and self.up(k):
            out += ['', '%%LINK-5-CHANGED: Interface %s, changed state to up' % IF_FULL[k],
                    '%%LINEPROTO-5-UPDOWN: Line protocol on Interface %s, changed state to up' % IF_FULL[k]]
        return out

    # --- справка
    def help(self, line):
        m = self.mode
        if line.strip() == '?':
            if m == 'user':
                return ['Exec commands:', '  <1-99>      Session number to resume', '  connect     Open a terminal connection',
                        '  disable     Turn off privileged commands', '  disconnect  Disconnect an existing network connection',
                        '  enable      Turn on privileged commands', '  exit        Exit from the EXEC',
                        '  logout      Exit from the EXEC', '  ping        Send echo messages',
                        '  resume      Resume an active network connection', '  show        Show running system information',
                        '  ssh         Open a secure shell client connection', '  telnet      Open a telnet connection',
                        '  terminal    Set terminal line parameters', '  traceroute  Trace route to destination']
            if m == 'priv':
                return ['Exec commands:', '  <1-99>      Session number to resume', '  clear       Reset functions',
                        '  clock       Manage the system clock', '  configure   Enter configuration mode',
                        '  connect     Open a terminal connection', '  copy        Copy from one file to another',
                        '  debug       Debugging functions (see also \'undebug\')', '  delete      Delete a file',
                        '  dir         List files on a filesystem', '  disable     Turn off privileged commands',
                        '  enable      Turn on privileged commands', '  erase       Erase a filesystem',
                        '  exit        Exit from the EXEC', '  ping        Send echo messages',
                        '  reload      Halt and perform a cold restart', '  show        Show running system information',
                        '  write       Write running configuration to memory, network, or terminal', '--More--']
            if m == 'if':
                return ['Interface configuration commands:', '  arp          Set arp type (arpa, probe, snap) or timeout',
                        '  bandwidth    Set bandwidth informational parameter', '  cdp          CDP interface subcommands',
                        '  delay        Specify interface throughput delay', '  description  Interface specific description',
                        '  duplex       Configure duplex operation.', '  exit         Exit from interface configuration mode',
                        '  ip           Interface Internet Protocol config commands', '  no           Negate a command or set its defaults',
                        '  shutdown     Shutdown the selected interface', '  speed        Configure speed operation.', '--More--']
        if line.strip() == 'show ?':
            base = ['  access-lists  List access lists', '  arp           Arp table', '  cdp           CDP information',
                    '  clock         Display the system clock', '  controllers   Interface controller status',
                    '  flash:        display information about flash: file system', '  history       Display the session command history',
                    '  hosts         IP domain-name, lookup style, nameservers, and host table',
                    '  interfaces    Interface status and configuration', '  ip            IP information',
                    '  protocols     Active network routing protocols', '  sessions      Information about Telnet connections',
                    '  terminal      Display terminal configuration parameters', '  users         Display information about terminal lines',
                    '  version       System hardware and software status']
            if self.mode == 'priv':
                base[10:10] = ['  running-config  Current operating configuration']
                base.append('  startup-config  Contents of startup configuration')
            return base
        if line.strip() == 'clock rate ?':
            return ['% Unrecognized command']
        return ['% Unrecognized command']

    # --- show
    def show(self, a):
        s = ' '.join(a).lower()
        if s == '?':
            return self.help('show ?')
        if s == 'version':
            return ['Cisco IOS Software, 1841 Software (C1841-ADVIPSERVICESK9-M), Version 12.4(15)T1, RELEASE SOFTWARE (fc2)',
                    'Technical Support: http://www.cisco.com/techsupport', 'Copyright (c) 1986-2007 by Cisco Systems, Inc.',
                    'Compiled Wed 18-Jul-07 06:21 by pt_team', '', 'ROM: System Bootstrap, Version 12.3(8r)T8, RELEASE SOFTWARE (fc1)', '',
                    '%s uptime is 3 minutes, 42 seconds' % self.host, 'System returned to ROM by power-on',
                    'System image file is "flash:c1841-advipservicesk9-mz.124-15.T1.bin"', '',
                    'Cisco 1841 (revision 5.0) with 114688K/16384K bytes of memory.', 'Processor board ID FTX0947Y18J',
                    '2 FastEthernet/IEEE 802.3 interface(s)', '191K bytes of NVRAM.',
                    '31360K bytes of ATA CompactFlash (Read/Write)', '', 'Configuration register is 0x2102']
        if s == 'clock':
            return ['*0:3:25.121 UTC Mon Mar 1 1993']
        if s == 'flash':
            return ['System flash directory:', 'File  Length   Name/status', '  3   33591768 c1841-advipservicesk9-mz.124-15.T1.bin',
                    '  2   28282    sigdef-category.xml', '  1   227537   sigdef-default.xml', '[33847587 bytes used, 30512637 available, 64360224 total]',
                    '63488K bytes of processor board System flash (Read/Write)']
        if s == 'history':
            return ['  ' + h for h in self.history[-10:]]
        if s == 'hosts':
            return ['Default Domain is not set', 'Name/address lookup uses domain service', 'Name servers are 255.255.255.255', '',
                    'Codes: UN - unknown, EX - expired, OK - OK, ?? - revalidate', '       temp - temporary, perm - permanent',
                    '       NA - Not Applicable None - Not defined', '', 'Host                      Port  Flags      Age Type   Address(es)']
        if s in ('interfaces', 'interface'):
            out = []
            for k in IF_FULL:
                out += self.if_detail(k)
            return out
        if s == 'sessions':
            return ['% No connections open']
        if s == 'terminal':
            return ['Line 0, Location: "", Type: ""', 'Length: 24 lines, Width: 80 columns', 'Baud rate (TX/RX) is 9600/9600, no parity, 2 stopbits, 8 databits',
                    'Status: PSI Enabled, Ready, Active, No Exit Banner', 'Capabilities: none', 'Modem state: Ready',
                    'Special Chars: Escape  Hold  Stop  Start  Disconnect  Activation', '                ^^x    none  -     -       none',
                    'Timeouts:      Idle EXEC    Idle Session   Modem Answer  Session   Dispatch', '               00:10:00     never          none     not set   not set',
                    'History is enabled, history size is 10.', 'Full user help is disabled', 'Allowed transports are telnet.  Preferred is telnet.']
        if s == 'users':
            return ['    Line       User       Host(s)              Idle       Location', '*  0 con 0                idle                 00:00:00', '',
                    '  Interface    User               Mode                     Idle     Peer Address']
        if s.startswith('controllers'):
            k = norm_if(a[1]) if len(a) > 1 else None
            if k:
                return ['Interface %s' % IF_FULL[k], 'Hardware is Fast Ethernet', 'ADDR: 0x%s' % self.mac[-4:]]
            return ['Interface FastEthernet0/0', 'Hardware is Fast Ethernet', 'Interface FastEthernet0/1', 'Hardware is Fast Ethernet']
        if s == 'protocols':
            out = ['Global values:', '  Internet Protocol routing is enabled']
            for k in IF_FULL:
                a1, a2 = self.state(k)
                out.append('%s is %s, line protocol is %s' % (IF_FULL[k], a1, a2))
            return out
        if s in ('running-config', 'configuration', 'startup-config') and self.mode == 'priv':
            if s != 'running-config' and not self.saved:
                return ['startup-config is not present']
            out = ['Building configuration...', '', 'Current configuration : 616 bytes', '!', 'version 12.4',
                   'no service timestamps log datetime msec', 'no service timestamps debug datetime msec',
                   'no service password-encryption', '!', 'hostname %s' % self.host, '!']
            if self.enable_pw:
                out += ['enable password %s' % self.enable_pw, '!']
            out += ['ip cef', 'no ipv6 cef', '!']
            for k in IF_FULL:
                out.append('interface %s' % IF_FULL[k])
                if self.ifs[k]['desc']:
                    out.append(' description %s' % self.ifs[k]['desc'])
                out += [' no ip address', ' duplex auto', ' speed auto']
                if self.ifs[k]['shut']:
                    out.append(' shutdown')
                out.append('!')
            out += ['ip classless', '!', 'line con 0', '!', 'line vty 0 4', ' login', '!', 'end']
            return out
        return ['', "% Invalid input detected at '^' marker.", '']

    def if_detail(self, k):
        a1, a2 = self.state(k)
        mac = self.mac[:-2] + ('%02x' % (int(self.mac[-2:], 16) + (1 if k == 'fa0/1' else 0)))
        out = ['%s is %s, line protocol is %s (%s)' % (IF_FULL[k], a1, a2, 'connected' if a2 == 'up' else 'disabled' if a1[0] == 'a' else 'notconnect'),
               '  Hardware is Lance, address is %s (bia %s)' % (mac, mac)]
        if self.ifs[k]['desc']:
            out.append('  Description: %s' % self.ifs[k]['desc'])
        out += ['  BW 100000 Kbit, DLY 100 usec,', '     reliability 255/255, txload 1/255, rxload 1/255',
                '  Encapsulation ARPA, loopback not set', '  ARP type: ARPA, ARP Timeout 04:00:00,',
                '  Last input 00:00:08, output 00:00:05, output hang never', '  Last clearing of "show interface" counters never',
                '  Input queue: 0/75/0 (size/max/drops); Total output drops: 0', '  Queueing strategy: fifo',
                '  Output queue :0/40 (size/max)', '  5 minute input rate 0 bits/sec, 0 packets/sec',
                '  5 minute output rate 0 bits/sec, 0 packets/sec', '     0 packets input, 0 bytes, 0 no buffer',
                '     0 packets output, 0 bytes, 0 underruns']
        return out

def session(router, lines):
    """Выполнить список команд; вернуть строки транскрипта (с приглашением)."""
    t = []
    for ln in lines:
        if isinstance(ln, tuple):          # ('pw', 'parol') - ввод пароля при enable
            t.append('Password: ')
            router.mode = 'priv'
            continue
        if ln in ('en', 'enable', 'ena') and router.mode == 'user' and router.enable_pw:
            t.append(router.prompt() + ln)
            t.append('Password: ')
            router.mode = 'priv'
            continue
        pr = router.prompt()
        out = router.run(ln)
        t.append(pr + ln)
        t.extend(out)
    t.append(router.prompt())
    return t
