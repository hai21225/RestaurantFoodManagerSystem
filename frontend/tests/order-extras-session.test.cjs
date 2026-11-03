const {test}=require('node:test'),assert=require('node:assert/strict');
const {context,PosSession,PaymentSession,snapshot,storage,auth,copy}=require('./f67-helpers.cjs');
require('node:vm').runInContext(require('node:fs').readFileSync(require('node:path').join(__dirname,'../js/common/order-extras-session.js'),'utf8'),context);
const {OrderExtrasSession}=context.window.restaurantOrderExtras;
function setup(role='Waiter'){
 const calls=[],store=storage(),a=auth(role),order={orderId:1,tableId:1,status:'Ready',items:[],totalAmount:100,promotionId:null,note:'',updatedAt:'1'},tables=[{tableId:1,isActive:true,status:'Occupied'},{tableId:2,isActive:true,status:'Available'}];
 const api={baseUrl:'/api',get:async p=>{calls.push(['get',p]);if(p==='/orders/1')return copy(order);if(p==='/restaurant-tables')return copy(tables);if(p.startsWith('/orders?'))return {total:0,items:[]};if(p==='/management/promotions')return [];throw Error(p);},patch:async(p,b)=>{calls.push(['patch',p,b]);if(p.endsWith('/table'))order.tableId=b.tableId;if(p.endsWith('/note'))order.note=b.note;},delete:async p=>calls.push(['delete',p])};
 return {s:new OrderExtrasSession({api,auth:a,storage:store,orderId:1}),api,a,store,calls,order,tables};
}
test('Phục vụ đọc đơn/bàn nhưng không gọi danh sách khuyến mãi AdminOnly',async()=>{const x=setup();await x.s.refresh();assert.ok(!x.calls.some(c=>c[1]==='/management/promotions'));assert.ok(!x.calls.some(c=>/^\/restaurant-tables\/\d/.test(c[1])));});
test('Cashier được xem, không được sửa ghi chú/chuyển bàn/khuyến mãi',async()=>{const x=setup('Cashier');await x.s.refresh();await assert.rejects(()=>x.s.command('note',{note:'x'},snapshot(x.s.order)),e=>e.status===403);assert.ok(x.calls.every(c=>c[0]==='get'));});
test('Ghi chú có thể xóa rỗng; actor lấy từ phiên, không nhận dữ liệu actor người nhập',async()=>{const x=setup();await x.s.refresh();await x.s.command('note',{note:'',changedBy:999},snapshot(x.s.order));const c=x.calls.find(c=>c[0]==='patch');assert.equal(c[1],'/orders/1/note');assert.equal(c[2].changedBy,9);assert.equal(c[2].note,'');});
test('Chuyển bàn đọc lại bàn đích và đơn; dùng trạng thái server sau lệnh',async()=>{const x=setup();await x.s.refresh();await x.s.command('move',{tableId:2},snapshot(x.s.order));assert.equal(x.s.order.tableId,2);assert.ok(x.calls.some(c=>c[1].startsWith('/orders?tableId=2')));});
test('Bàn có khách hoặc đơn chen vào thì không gửi lệnh chuyển',async()=>{for(const occupied of [true,false]){const x=setup();await x.s.refresh();if(occupied)x.tables[1].status='Occupied';else{const get=x.api.get;x.api.get=async p=>p.startsWith('/orders?')?{items:[{status:'Ready'}],total:1}:get(p);}await assert.rejects(()=>x.s.command('move',{tableId:2},snapshot(x.s.order)));assert.ok(x.calls.every(c=>c[0]==='get'));}});
test('Đơn Paid hoặc snapshot thay đổi không được áp khuyến mãi',async()=>{for(const change of [{status:'Paid'},{updatedAt:'2'}]){const x=setup();await x.s.refresh();const old=snapshot(x.s.order);Object.assign(x.order,change);await assert.rejects(()=>x.s.command('apply',{promotionId:1},old));assert.ok(x.calls.every(c=>c[0]==='get'));}});
test('Gỡ khuyến mãi phải đúng promotionId hiện tại',async()=>{const x=setup();x.order.promotionId=2;await x.s.refresh();await assert.rejects(()=>x.s.command('remove',{promotionId:1},snapshot(x.s.order)));await x.s.command('remove',{promotionId:2},snapshot(x.s.order));assert.equal(x.calls.find(c=>c[0]==='delete')[1],'/orders/1/promotion/2');});
test('Mất ACK chuyển bàn giữ pending; tải lại theo orderId, không chuyển lại',async()=>{const x=setup();await x.s.refresh();const patch=x.api.patch;x.api.patch=async(p,b)=>{await patch(p,b);throw new Error('ACK');};await assert.rejects(()=>x.s.command('move',{tableId:2},snapshot(x.s.order)));const r=new OrderExtrasSession({api:x.api,auth:x.a,storage:x.store,orderId:999});assert.equal(r.orderId,1);await r.review(()=>r.load());assert.equal(r.order.tableId,2);assert.equal(x.calls.filter(c=>c[0]==='patch').length,1);});
test('POS/Thanh toán đang pending thì không gửi thao tác F7',async()=>{const x=setup();await x.s.refresh();x.store.setItem('restaurant.posPending',JSON.stringify({userId:9}));await assert.rejects(()=>x.s.command('note',{note:'x'},snapshot(x.s.order)));assert.ok(x.calls.every(c=>c[0]==='get'));});
test('F7 mất phản hồi chặn cả sửa món và thu/hoàn tiền trong cùng phiên',async()=>{
 const store=storage(),a=auth(),api={baseUrl:'/api'};store.setItem('restaurant.orderExtrasPending',JSON.stringify({userId:9,realm:'/api',context:{orderId:1}}));
 const pos=new PosSession({tableId:1,api,auth:a,storage:store});assert.throws(()=>pos.assertWritable(),/Tiện ích đơn/);
 const pay=new PaymentSession({api,auth:a,storage:store});assert.match(pay.block(),/Tiện ích đơn/);await assert.rejects(()=>pay.command('refund',{}),/Tiện ích đơn/);
});
test('Pending F7 ở API khác không khóa nhầm POS/Thu ngân hiện tại',()=>{
 const store=storage(),a=auth(),api={baseUrl:'/api'};store.setItem('restaurant.orderExtrasPending',JSON.stringify({userId:9,realm:'/other'}));
 assert.equal(new PosSession({tableId:1,api,auth:a,storage:store}).hasExtraPending(),false);assert.equal(new PaymentSession({api,auth:a,storage:store}).hasExtraPending(),false);
});
