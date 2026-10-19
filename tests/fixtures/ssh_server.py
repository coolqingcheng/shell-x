"""Loopback-only fixture; shell echoes bytes, SFTP is restricted to a test directory."""
import errno, json, os, posixpath, socket, stat, sys, threading
from pathlib import Path
import paramiko

base = Path(sys.argv[1]).resolve()
root = base / 'remote'
root.mkdir(parents=True, exist_ok=True)
(root / 'hello.txt').write_text('ShellX 测试文件\n', encoding='utf-8')
(root / '.hidden').write_text('hidden', encoding='utf-8')
(root / 'folder').mkdir(exist_ok=True)
host_key, client_key = paramiko.RSAKey.generate(2048), paramiko.RSAKey.generate(2048)
client_key.write_private_key_file(str(base / 'client-key'))
client_key.write_private_key_file(str(base / 'client-key-encrypted'), password='fixture-passphrase')
modes = {}
links = {}
shell_transports = set()
shell_transports_lock = threading.Lock()
paramiko.util.log_to_file(str(base/'server-debug.log'))

def bridge(a, b):
    def copy(source, target):
        try:
            while chunk := source.recv(65536): target.sendall(chunk)
        except (OSError, EOFError): pass
        finally:
            try: target.shutdown(socket.SHUT_WR)
            except (OSError, EOFError): pass
    threading.Thread(target=copy, args=(a,b), daemon=True).start()
    threading.Thread(target=copy, args=(b,a), daemon=True).start()

class Files(paramiko.SFTPServerInterface):
    def local(self, path):
        path = posixpath.normpath('/' + path.lstrip('/'))
        for link,target_path in list(links.items()):
            if path == link or path.startswith(link+'/'):
                path = posixpath.normpath(target_path + path[len(link):]); break
        target = (root / path.lstrip('/')).resolve()
        if os.path.commonpath([str(root),str(target)]) != str(root): raise PermissionError(path)
        return target
    def attrs(self, path):
        value = paramiko.SFTPAttributes.from_stat(path.stat())
        value.st_mode = (stat.S_IFDIR if path.is_dir() else stat.S_IFREG) | modes.get(str(path), 0o755 if path.is_dir() else 0o644)
        value.st_uid = value.st_gid = 1000
        return value
    def canonicalize(self, path): return '/' + self.local(path).relative_to(root).as_posix().removeprefix('.')
    def list_folder(self, path):
        try:
            result = []
            for child in self.local(path).iterdir():
                item = self.attrs(child); item.filename = child.name; result.append(item)
            for link in list(links):
                if posixpath.dirname(link) == posixpath.normpath(path):
                    item = self.link_attrs(); item.filename=posixpath.basename(link); result.append(item)
            return result
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno or errno.EACCES)
    def stat(self, path):
        try: return self.attrs(self.local(path))
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno or errno.EACCES)
    def link_attrs(self):
        value=paramiko.SFTPAttributes(); value.st_mode=stat.S_IFLNK|0o777; value.st_size=0
        value.st_uid=value.st_gid=1000; value.st_atime=value.st_mtime=0
        return value
    def lstat(self,path):
        return self.link_attrs() if posixpath.normpath(path) in links else self.stat(path)
    def symlink(self,target,path):
        links[posixpath.normpath(path)]=target; return paramiko.SFTP_OK
    def readlink(self,path): return links.get(posixpath.normpath(path),paramiko.SFTP_NO_SUCH_FILE)
    def open(self, path, flags, attr):
        try:
            target = self.local(path)
            fd = os.open(target, flags | getattr(os,'O_BINARY',0), 0o600)
            stream = os.fdopen(fd, 'r+b' if flags & os.O_RDWR else 'wb' if flags & os.O_WRONLY else 'rb', buffering=0)
            handle = paramiko.SFTPHandle(flags); handle.readfile = handle.writefile = stream
            handle.stat = lambda: self.attrs(target)
            def change(value):
                if value.st_size is not None: stream.truncate(value.st_size)
                if value.st_mode is not None: modes[str(target)] = value.st_mode & 0o7777
                return paramiko.SFTP_OK
            handle.chattr = change
            return handle
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno or errno.EACCES)
    def operation(self, operation):
        try: operation(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno or errno.EACCES)
    def mkdir(self,path,attr): return self.operation(lambda:self.local(path).mkdir())
    def remove(self,path):
        if posixpath.normpath(path) in links:
            del links[posixpath.normpath(path)]; return paramiko.SFTP_OK
        return self.operation(lambda:self.local(path).unlink())
    def rmdir(self,path): return self.operation(lambda:self.local(path).rmdir())
    def rename(self,old,new):
        if posixpath.normpath(old) in links:
            links[posixpath.normpath(new)]=links.pop(posixpath.normpath(old)); return paramiko.SFTP_OK
        return self.operation(lambda:self.local(old).rename(self.local(new)))
    def chattr(self,path,attr):
        target = self.local(path)
        if attr.st_mode is not None: modes[str(target)] = attr.st_mode & 0o7777
        if attr.st_size is not None:
            with target.open('r+b') as stream: stream.truncate(attr.st_size)
        return paramiko.SFTP_OK

class Server(paramiko.ServerInterface):
    def __init__(self,transport): self.transport=transport; self.destinations={}; self.listeners={}; self.sample=0
    def check_auth_password(self,user,password): return paramiko.AUTH_SUCCESSFUL if user=='shellx' and password=='fixture-password' else paramiko.AUTH_FAILED
    def check_auth_publickey(self,user,key): return paramiko.AUTH_SUCCESSFUL if user=='shellx' and key==client_key else paramiko.AUTH_FAILED
    def get_allowed_auths(self,user): return 'password,publickey'
    def check_channel_request(self,kind,channel_id): return paramiko.OPEN_SUCCEEDED if kind=='session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
    def check_channel_pty_request(self,*args): return True
    def check_channel_window_change_request(self,*args): return True
    def check_channel_exec_request(self,channel,command):
        # 仅返回测试数据，不执行收到的任何命令。
        if b'__SX_CPU__' not in command: return False
        self.sample+=1; n=self.sample
        payload=f'''__SX_SYSTEM__
fixture-linux
Linux 6.1.0 x86_64
Debian GNU/Linux 12 (test fixture)
127.0.0.1
__SX_UPTIME__
90061 1000
__SX_LOAD__
0.12 0.08 0.05 1/20 123
__SX_CPU__
cpu {100+n*20} 0 {100+n*10} {800+n*70} 0 0 0 0 0 0
__SX_MEMORY__
MemTotal: 8388608 kB
MemAvailable: 6291456 kB
SwapTotal: 1048576 kB
SwapFree: 1048576 kB
__SX_NETWORK__
eth0: {100000+n*4096} 0 0 0 0 0 0 0 {20000+n*1024} 0 0 0 0 0 0 0
lo: 100 0 0 0 0 0 0 0 100 0 0 0 0 0 0 0
__SX_PROCESS__
123 1.2 10240 sshd
456 0.2 5120 systemd
__SX_DISK__
Filesystem 1024-blocks Used Available Capacity Mounted on
/dev/sda1 20971520 4194304 16777216 20% /
'''
        def result():
            channel.sendall(payload.encode()); channel.send_exit_status(0); channel.close()
        threading.Thread(target=result,daemon=True).start(); return True
    def check_channel_shell_request(self,channel):
        with shell_transports_lock: shell_transports.add(self.transport)
        def shell():
            channel.sendall('\x1b[32mShellX 本地测试服务器\x1b[0m\r\nshellx@test:~$ '.encode())
            try:
                while data:=channel.recv(4096):
                    if b'__SX_DISCONNECT_OTHER_TRANSPORTS__' in data:
                        with shell_transports_lock: others=list(shell_transports-{self.transport})
                        for other in others: other.close()
                    if b'__SX_DISCONNECT_TRANSPORT__' in data:
                        self.transport.close(); break
                    channel.sendall(b'received: '+data+b'\r\nshellx@test:~$ ')
            except (OSError,EOFError): pass
        threading.Thread(target=shell,daemon=True).start(); return True
    def check_channel_direct_tcpip_request(self,channel_id,origin,destination):
        if destination[0] not in ('127.0.0.1','localhost'): return paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
        # SSH open 确认必须发生在目标 TCP 建立之后，才能验证目标不可达的检测行为。
        try:
            target=socket.create_connection(destination,timeout=2)
            target.settimeout(None) # 仅建立连接限时，已转发的长连接不能两秒无数据就断开。
            self.destinations[channel_id]=target
        except OSError: return paramiko.OPEN_FAILED_CONNECT_FAILED
        return paramiko.OPEN_SUCCEEDED
    def check_port_forward_request(self,address,port):
        if address!='127.0.0.1': return False
        listener=socket.socket()
        try: listener.bind((address,port)); listener.listen()
        except OSError: listener.close(); return False
        actual=listener.getsockname()[1]
        self.listeners[(address,actual)]=listener
        def forward(sock,origin):
            try: bridge(sock,self.transport.open_forwarded_tcpip_channel(origin,(address,actual)))
            except (OSError,EOFError,paramiko.SSHException): sock.close()
        def accept():
            while self.transport.is_active():
                try: sock,origin=listener.accept()
                except OSError: break
                # 每个请求独立等待 SSH 确认，单次目标失败不能阻塞整个监听的后续连接。
                threading.Thread(target=forward,args=(sock,origin),daemon=True).start()
        threading.Thread(target=accept,daemon=True).start(); return actual
    def cancel_port_forward_request(self,address,port):
        listener=self.listeners.pop((address,port),None)
        if listener: listener.close()

def connection(sock):
    transport=paramiko.Transport(sock); server=Server(transport)
    try:
        transport.add_server_key(host_key); transport.set_subsystem_handler('sftp',paramiko.SFTPServer,Files); transport.start_server(server=server)
        while transport.is_active():
            channel=transport.accept(1)
            if channel and channel.chanid in server.destinations:
                try: bridge(channel,server.destinations.pop(channel.chanid))
                except OSError: channel.close()
    except (OSError,EOFError,paramiko.SSHException): pass
    finally:
        transport.close()
        with shell_transports_lock: shell_transports.discard(transport)
        for listener in server.listeners.values(): listener.close()
        for pending_socket in server.destinations.values(): pending_socket.close()

listener=socket.socket(); listener.bind(('127.0.0.1',0)); listener.listen()
config={'Port':listener.getsockname()[1],'Root':str(root),'PrivateKey':str(base/'client-key'),'EncryptedPrivateKey':str(base/'client-key-encrypted')}
(base/'fixture.json').write_text(json.dumps(config),encoding='utf-8')
print(json.dumps({'fixture':str(base/'fixture.json'),'port':config['Port']}),flush=True)
while True:
    sock,_=listener.accept(); threading.Thread(target=connection,args=(sock,),daemon=True).start()
