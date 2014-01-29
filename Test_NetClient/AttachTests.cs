﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Breeze.NetClient;
using Breeze.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Foo;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Test_NetClient {

  [TestClass]
  public class AttachTests {

    private Task<EntityManager> _emTask = null;
    private EntityManager _em1;
    private static MetadataStore __metadataStore;

    [TestInitialize]
    public void TestInitializeMethod() {
      _emTask = SetUpAsync();
    }

    public async Task<EntityManager> SetUpAsync() {
      var serviceName = "http://localhost:7150/breeze/NorthwindIBModel/";
      
      if (__metadataStore == null) {
        _em1 = new EntityManager(serviceName);
        await _em1.FetchMetadata();
        __metadataStore = _em1.MetadataStore;
      } else {
        _em1 = new EntityManager(serviceName, __metadataStore);
      }
      return _em1;
      
    }

    [TestCleanup]
    public void TearDown() {
      
    }

    [TestMethod]
    public async Task RejectChanges() {
      await _emTask;


      var prod1 = new Product();
      prod1.Discontinued = true;
      _em1.AttachEntity(prod1, EntityState.Unchanged);
      prod1.Discontinued = false;
      prod1.EntityAspect.RejectChanges();
      
      Assert.IsTrue(prod1.Discontinued == true, "should have returned to initial state");
    }

    [TestMethod]
    public async Task CreateEntity() {
      await _emTask;

      var emp1 = _em1.CreateEntity(typeof(Employee));
      Assert.IsTrue(emp1.EntityAspect.EntityState == EntityState.Added);
      var emp3 = _em1.CreateEntity<Employee>(EntityState.Detached);
      emp3.LastName = "Smith";
      Assert.IsTrue(emp3.EntityAspect.EntityState.IsDetached());
      Assert.IsTrue(emp3.LastName == "Smith");
    }

    [TestMethod]
    public async Task AttachEntityDefaultValues() {
      // Default values when entity is first created.
      await _emTask;

      var employeeType = _em1.MetadataStore.GetEntityType(typeof(Employee));
      var empIdProp = employeeType.GetDataProperty(TestFns.EmployeeKeyName);

      var emp = _em1.CreateEntity<Employee>(EntityState.Unchanged);
      Assert.IsTrue(emp.EmployeeID == 0, "id should be zero at creation");
      var agkType = employeeType.AutoGeneratedKeyType;
      emp.EntityAspect.Detach();
      Assert.IsTrue(emp.EntityAspect.IsDetached);
      // manager should NOT replace '0' with generated temp id 
      _em1.AttachEntity(emp, EntityState.Unchanged);
      Assert.IsTrue(emp.EmployeeID == 0, "should still be 0");
      emp.EntityAspect.Detach();
      _em1.AttachEntity(emp, EntityState.Added);
      Assert.IsTrue(emp.EmployeeID != 0, "should not be be 0");
    }

    [TestMethod]
    public async Task AddToNavSet() {
        await _emTask;
        
        var customer = new Customer();
        var order = new Order();
        _em1.AttachEntity(customer);
        var origOrders = customer.Orders;
        Assert.IsTrue(origOrders.Count == 0);
        origOrders.Add(order);
        // next line won't compile - this is good!
        // customer.Orders = new NavigationSet<Order>();
        Assert.IsTrue(origOrders.Count == 1);
        Assert.IsTrue(customer.Orders == origOrders, "should be same collection");
        Assert.IsTrue(order.EntityAspect.IsAttached, "should be attached");
        Assert.IsTrue(order.Customer == customer, "order.Customer should = customer");
    }
    
    //test("cannot attach an entity created by a different metadataStore", 1, function () {
    //    var em = newEm();
    //    var customerType = em.metadataStore.getEntityType("Customer");
    //    var customer = customerType.createEntity();
    //    var newMs = MetadataStore.importMetadata(em.metadataStore.exportMetadata());
    //    var em2 = newEm(newMs);
    //    try {
    //        em2.attachEntity(customer);
    //        ok(false, "should not get here");
    //    } catch (e) {
    //        ok(e.message.indexOf("MetadataStore"));
    //    }
        
    //});

     // can attach a detached entity to a different manager via attach/detach
    [TestMethod]
    public async Task AttachToDifferentManager() {
      await _emTask;

      var cust = _em1.CreateEntity<Customer>(EntityState.Unchanged);
      var order = _em1.CreateEntity<Order>(EntityState.Unchanged);
      cust.Orders.Add(order);
      Assert.IsTrue(cust.Orders.Count == 1);
      var em2 = new EntityManager(_em1);
      try {
        em2.AttachEntity(cust);
        Assert.Fail("should not get here");
      } catch {
        // expected
      }

      _em1.DetachEntity(cust);
      Assert.IsTrue(order.Customer == null);
      Assert.IsTrue(cust.Orders.Count == 0);
      em2.AttachEntity(cust);
      Assert.IsTrue(cust.EntityAspect.EntityManager == em2);

    }
    
    // can attach a detached entity to a different manager via clear
    [TestMethod]
    public async Task AttachEmViaDetach() {
      await _emTask;

        var cust = new Customer();
        cust.EntityAspect.SetValue(TestFns.CustomerKeyName, Guid.NewGuid());
        Assert.IsTrue(cust.EntityAspect.IsDetached, "should be detached");
        _em1.AttachEntity(cust);
        Assert.IsTrue(cust.EntityAspect.IsAttached, "should be attached");
        _em1.Clear(); // will detach cust

        Assert.IsTrue(cust.EntityAspect.IsDetached, "should be detached - again");
        Assert.IsTrue(cust.EntityAspect.EntityManager == _em1, "should still be associated with em1");
        // therefore this should be ok
        var em2 = new EntityManager(_em1);
        em2.AttachEntity(cust);
        Assert.IsTrue(cust.EntityAspect.EntityManager == em2, "should be on em2");
    }

    
    // setting child's parent entity null removes it from old parent
    [TestMethod]
    public async Task SetParentEntityToNull() {
      await _emTask;

      var cust = _em1.CreateEntity<Customer>(EntityState.Unchanged);
      var order = _em1.CreateEntity<Order>();
      order.Customer = cust;
      Assert.IsTrue(order.Customer == cust, "should be set");
      Assert.IsTrue(cust.Orders.First() == order, "should be part of collection" );
      order.Customer = null;
      Assert.IsTrue(order.Customer == null, "should not be set");
      Assert.IsTrue(cust.Orders.Count == 0, "should not be part of collection" );

    }

    // unidirectional attach - n->1
    [TestMethod]
    public async Task UnidirectionalAttachFk() {
      await _emTask;
      if (TestFns.DEBUG_MONGO) {
        Assert.Inconclusive("NA for Mongo - Order/OrderDetail");
        return;
      }

      var od1 = new OrderDetail();
      var prod1 = new Product();
      od1.ProductID = -99;
      _em1.AttachEntity(od1);
      _em1.AttachEntity(prod1);
      Assert.IsTrue(od1.Product == null, "Product should be null");
      prod1.ProductID = 2;
      od1.ProductID = 2;
      Assert.IsTrue(od1.Product == prod1, "should now point to product");

      var od2 = new OrderDetail();
      var prod2 = new Product();
      od2.ProductID = -88;
      _em1.AttachEntity(od2);
      _em1.AttachEntity(prod2);
      Assert.IsTrue(od2.Product == null, "Product should be null - again");
      // same as above but different order
      od2.ProductID = 3;
      // should now have an unresolved parent.
      prod2.ProductID = 3;
      
      Assert.IsTrue(od2.Product == prod2, "should now point to product - again");
    }


    // unidirectional attach - 1->n
    [TestMethod]
    public async Task UnidirectionalAttach1ToN() {
      await _emTask;

      if (TestFns.DEBUG_MONGO || TestFns.DEBUG_ODATA) {
        Assert.Inconclusive( "NA for Mongo or OData - TimeList and Timegroup not yet added");
      }
      
      var tl1 = _em1.CreateEntity<TimeLimit>();
      var tl2 = _em1.CreateEntity<TimeLimit>();
      var tg1 = _em1.CreateEntity<TimeGroup>();
      var id1 = tg1.Id;
      tl1.TimeGroupId = id1;
      Assert.IsTrue(tg1.TimeLimits.Count == 1 
        && tg1.TimeLimits.Contains(tl1), "should be connected");
      tl2.TimeGroupId = id1;
      Assert.IsTrue(tg1.TimeLimits.Count == 2 
        && tg1.TimeLimits.Contains(tl2), "another should be connected");

    
    }

    // unidirectional attach - 1->n
    [TestMethod]
    public async Task UnidirectionalAttach1ToN_v2() {
      await _emTask;

      if (TestFns.DEBUG_MONGO || TestFns.DEBUG_ODATA) {
        Assert.Inconclusive( "NA for Mongo or OData - TimeList and Timegroup not yet added");
      }
      
      var tl1 = _em1.CreateEntity<TimeLimit>();
      var tl2 = _em1.CreateEntity<TimeLimit>();
      var tg1 = _em1.CreateEntity<TimeGroup>();
      Assert.IsTrue(tg1.TimeLimits.Count == 0, "should be no Timelimits");
      tg1.TimeLimits.Add(tl1);
      Assert.IsTrue(tg1.TimeLimits.Count == 1 
        && tg1.TimeLimits.Contains(tl1), "should be connected");
      tg1.TimeLimits.Add(tl2);
      Assert.IsTrue(tg1.TimeLimits.Count == 2 
        && tg1.TimeLimits.Contains(tl2), "another should be connected");

      // add 1 that is already there
      tg1.TimeLimits.Add(tl1);
      Assert.IsTrue(tg1.TimeLimits.Count == 2 
        && tg1.TimeLimits.Contains(tl1), "length should not change when adding a dup");
    }

    // primary key fixup
    [TestMethod]
    public async Task PrimaryKeyFixup() {
      await _emTask;

      var prod = new Product();
      _em1.AttachEntity(prod);
      var origProdId = prod.ProductID;
      var ek = prod.EntityAspect.EntityKey;
      var sameProd = _em1.FindEntityByKey(ek);
      Assert.IsTrue(prod == sameProd, "should be the same product");
      var sameProd2 = _em1.FindEntityByKey<Product>(origProdId);
      Assert.IsTrue(prod == sameProd2, "should be the same product-again");
      prod.ProductID = 7;
      var notSameProd = _em1.FindEntityByKey(ek);
      Assert.IsTrue(notSameProd == null);
      var sameProd3 = _em1.FindEntityByKey(prod.EntityAspect.EntityKey);
      Assert.IsTrue(prod == sameProd2, "should be the same product-again 2");
    }
    
    // changing FK to null removes it from old parent
    [TestMethod]
    public async Task FkSetToNull() {
      await _emTask;

      var cust = _em1.CreateEntity<Customer>(EntityState.Unchanged);
      var order1 = _em1.CreateEntity<Order>();
      order1.Customer = cust;
      Assert.IsTrue(order1.Customer == cust, "should be customer");
      Assert.IsTrue(cust.Orders.Contains(order1), "should contain order1");

      var order2 = new Order();
      order2.Customer = cust;
      Assert.IsTrue(order2.EntityAspect.IsAttached && order2.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(order2.Customer == cust, "should be customer - again");
      Assert.IsTrue(cust.Orders.Contains(order2), "should contain order2");

      order1.CustomerID = null;
      Assert.IsTrue(order1.Customer == null, "should be null");
      Assert.IsTrue(!cust.Orders.Contains(order1), "should not contain order1");

    }

    
    // add, detach and readd
    [TestMethod]
    public async Task AddDetachReadd() {
      await _emTask;

      var order = _em1.CreateEntity<Order>();
      Assert.IsTrue(order.EntityAspect.IsAttached && order.EntityAspect.EntityState.IsAdded());
      _em1.DetachEntity(order);
      Assert.IsTrue(order.EntityAspect.IsDetached);
      _em1.AttachEntity(order, EntityState.Added);
      Assert.IsTrue(order.EntityAspect.IsAttached && order.EntityAspect.EntityState.IsAdded());
    }

    // attach, detach and reattach
    [TestMethod]
    public async Task AttachDetachReattach() {
      await _emTask;

      var order = _em1.CreateEntity<Order>(EntityState.Unchanged);
      Assert.IsTrue(order.EntityAspect.IsAttached && order.EntityAspect.EntityState.IsUnchanged());
      _em1.DetachEntity(order);
      Assert.IsTrue(order.EntityAspect.IsDetached);
      _em1.AttachEntity(order);
      Assert.IsTrue(order.EntityAspect.IsAttached && order.EntityAspect.EntityState.IsUnchanged());
    }
    
    // exception if set nav to entity with different manager
    [TestMethod]
    public async Task ErrorOnNavAttach() {
      await _emTask;
    
      var order = _em1.CreateEntity<Order>(EntityState.Unchanged);
    
      var em2 = new EntityManager(_em1);
      var cust = em2.CreateEntity<Customer>(EntityState.Unchanged);
      Assert.IsTrue(order.EntityAspect.EntityManager != cust.EntityAspect.EntityManager, "should not be the same manager");
      try {
        order.Customer = cust;
        Assert.Fail("should not get here");
      } catch (Exception e) {
        Assert.IsTrue(e.Message.Contains("EntityManager"), "message should mention 'EntityManager'");
      }
      cust.EntityAspect.Detach();
      order.Customer = cust;
      Assert.IsTrue(order.EntityAspect.EntityManager == cust.EntityAspect.EntityManager, "should be the same manager");
      Assert.IsTrue(cust.Orders.Contains(order) && order.Customer == cust, "should be properly connected");
    }

    // exception if set nav to entity with different manager
    [TestMethod]
    public async Task ErrorOnAttachMultiple() {
      await _emTask;
    
      var order = _em1.CreateEntity<Order>(EntityState.Unchanged);
      var em2 = new EntityManager(_em1);
      try {
        em2.AttachEntity(order);
        Assert.Fail("should not get here");
      } catch (Exception e) {
        Assert.IsTrue(e.Message.Contains("EntityManager"), "message should mention 'EntityManager'");
      }
    }
      
       
    // rejectChanges on added entity
    [TestMethod]
    public async Task RejectChangesOnAdd() {
      await _emTask;
      var order = _em1.CreateEntity<Order>();
      Assert.IsTrue(order.EntityAspect.EntityState.IsAdded(), "should be in Added state");
      Assert.IsTrue(_em1.HasChanges(), "should have some changes");
      var ents = _em1.GetEntities();
      Assert.IsTrue(ents.Count() == 1);
      order.EntityAspect.RejectChanges();
      Assert.IsTrue(order.EntityAspect.IsDetached);
      Assert.IsTrue(!_em1.HasChanges(), "should not have any changes");
      ents = _em1.GetEntities();
      Assert.IsTrue(ents.Count() == 0);

    }
    
    // delete added entity
    [TestMethod]
    public async Task DeleteAdded() {
      await _emTask;
      var order = _em1.CreateEntity<Order>();
      Assert.IsTrue(order.EntityAspect.EntityState.IsAdded(), "should be in Added state");
      Assert.IsTrue(_em1.HasChanges(), "should have some changes");
      order.EntityAspect.Delete();
      Assert.IsTrue(order.EntityAspect.IsDetached);
      Assert.IsTrue(!_em1.HasChanges(), "should not have any changes");
      var ents = _em1.GetEntities();
      Assert.IsTrue(ents.Count() == 0);
    }

    // add entity - no key
    [TestMethod]
    public async Task AddEntityNoOrPartialKey() {
      await _emTask;

      if (TestFns.DEBUG_MONGO) {
        Assert.Inconclusive("NA for Mongo - OrderDetail");
        return;
      }
      var od = new OrderDetail();
      try {
        _em1.AttachEntity(od, EntityState.Added);
        Assert.Fail("should not get here");
      } catch (Exception e) {
        Assert.IsTrue(e.Message.Contains("key"), "error message should contain 'key'");
      }

      // only need to set part of the key
      od.OrderID = 999;
      _em1.AttachEntity(od, EntityState.Added);
      Assert.IsTrue(od.EntityAspect.EntityState.IsAdded());

      try {
        var od2 =_em1.CreateEntity<OrderDetail>(EntityState.Added);
        Assert.Fail("should not get here");
      } catch (Exception e) {
        Assert.IsTrue(e.Message.Contains("key"), "error message should contain 'key'");
      }
    }

    // add child
    [TestMethod]
    public async Task AddToNavCollection() {
      await _emTask;

      var cust1 = new Customer();
      var order1 = new Order();
      _em1.AttachEntity(cust1, EntityState.Added);
      Assert.IsTrue(cust1.EntityAspect.HasTemporaryKey, "should have a temp key");
      var orders = cust1.Orders;
      Assert.IsTrue(orders.ParentEntity == cust1, "ParentEntity should be set");
      Assert.IsTrue(orders.NavigationProperty == cust1.EntityAspect.EntityType.GetNavigationProperty("Orders"), "NavProperty should be set");

      NotifyCollectionChangedEventArgs changeArgs = null;
      orders.CollectionChanged += (s, e) => {
        changeArgs = e;
      };
      orders.Add(order1);
      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded(), "should be added");
      Assert.IsTrue(orders.Contains(order1), "should contain order");
      Assert.IsTrue(order1.Customer == cust1, "should be connected");
      Assert.IsTrue(changeArgs != null, "changeArgs should not be null");
      Assert.IsTrue(changeArgs.Action == NotifyCollectionChangedAction.Add);
      Assert.IsTrue(changeArgs.NewItems.Contains(order1), "change should mention order1");

    }


    // detach child
    [TestMethod]
    public async Task RemoveFromNavCollection() {
      await _emTask;

      var cust1 = new Customer();
      var order1 = new Order();
      var order2 = new Order();
      _em1.AddEntity(cust1);
      var orders = cust1.Orders;
      orders.Add(order1);
      orders.Add(order2);
      
      var collectionChangedList = new List<NotifyCollectionChangedEventArgs>();
      orders.CollectionChanged += (s, e) => {
        collectionChangedList.Add(e);
      };
      var propChangedList = new List<PropertyChangedEventArgs>();
      ((INotifyPropertyChanged)order1).PropertyChanged += (s, e) => {
        propChangedList.Add(e);
      };
      orders.Remove(order1);
      Assert.IsTrue(collectionChangedList.Last().Action == NotifyCollectionChangedAction.Remove);
      Assert.IsTrue(collectionChangedList.Last().OldItems.Contains(order1), "change event should contain order1");
      // TODO: this is not yet happening but it should
      // Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "Customer"), "propChange should mention Customer");
      Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "CustomerID"), "propChange should mention CustomerID");

      Assert.IsTrue(!orders.Contains(order1), "order1 should have been removed");
      Assert.IsTrue(order1.Customer == null, "Customer should be null");
      Assert.IsTrue(order1.CustomerID == null, "CustomerID should be null"); // null because not required.
      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(orders.Count == 1, "count should be 1");

      collectionChangedList.Clear();
      propChangedList.Clear();
      order1.Customer = cust1;
      Assert.IsTrue(collectionChangedList.Last().Action == NotifyCollectionChangedAction.Add);
      Assert.IsTrue(collectionChangedList.Last().NewItems.Contains(order1), "change event should contain order1");
      // Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "Customer"), "propChange should mention Customer");
      Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "CustomerID"), "propChange should mention CustomerID");

      Assert.IsTrue(orders.Contains(order1), "order1 should be back");
      Assert.IsTrue(order1.Customer == cust1, "Customer should be back");
      Assert.IsTrue(order1.CustomerID == cust1.CustomerID, "CustomerID should be back"); // null because not required.
      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(orders.Count == 2, "count should be 2");

    }

    [TestMethod]
    public async Task DetachFromNavCollection() {
      await _emTask;

      var cust1 = new Customer();
      var order1 = new Order();
      var order2 = new Order();
      _em1.AddEntity(cust1);
      var orders = cust1.Orders;
      orders.Add(order1);
      orders.Add(order2);

      var collectionChangedList = new List<NotifyCollectionChangedEventArgs>();
      orders.CollectionChanged += (s, e) => {
        collectionChangedList.Add(e);
      };
      var propChangedList = new List<PropertyChangedEventArgs>();
      ((INotifyPropertyChanged)order1).PropertyChanged += (s, e) => {
        propChangedList.Add(e);
      };
      order1.EntityAspect.Detach();
      Assert.IsTrue(collectionChangedList.Last().Action == NotifyCollectionChangedAction.Remove);
      Assert.IsTrue(collectionChangedList.Last().OldItems.Contains(order1), "change event should contain order1");
      
      
      Assert.IsTrue(propChangedList.Count == 0, "Detaching an entity will not create a propertyChange event");

      Assert.IsTrue(!orders.Contains(order1), "order1 should have been removed");
      Assert.IsTrue(order1.Customer == null, "Customer should be null");
      Assert.IsTrue(order1.CustomerID == null); // null because not required.
      Assert.IsTrue(order1.EntityAspect.EntityState.IsDetached());
      Assert.IsTrue(orders.Count == 1, "count should be 1");

      collectionChangedList.Clear();
      propChangedList.Clear();
      order1.Customer = cust1;
      Assert.IsTrue(collectionChangedList.Last().Action == NotifyCollectionChangedAction.Add);
      Assert.IsTrue(collectionChangedList.Last().NewItems.Contains(order1), "change event should contain order1");
      // Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "Customer"), "propChange should mention Customer");
      Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "CustomerID"), "propChange should mention CustomerID");

      Assert.IsTrue(orders.Contains(order1), "order1 should be back");
      Assert.IsTrue(order1.Customer == cust1, "Customer should be back");
      Assert.IsTrue(order1.CustomerID == cust1.CustomerID, "CustomerID should be back"); // null because not required.
      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(orders.Count == 2, "count should be 2");

    }

    [TestMethod]
    public async Task ChangeParent1ToN() {
      await _emTask;

      var cust1 = new Customer();
      var cust2 = new Customer();
      
      var order1 = new Order();
      var order2 = new Order();
      _em1.AddEntity(cust1);
      _em1.AddEntity(cust2);
      
      cust1.Orders.Add(order1);
      cust1.Orders.Add(order2);

      var cust1CollChangedList = new List<NotifyCollectionChangedEventArgs>();
      cust1.Orders.CollectionChanged += (s, e) => {
        cust1CollChangedList.Add(e);
      };
      var cust2CollChangedList = new List<NotifyCollectionChangedEventArgs>();
      cust2.Orders.CollectionChanged += (s, e) => {
        cust2CollChangedList.Add(e);
      };

      var propChangedList = new List<PropertyChangedEventArgs>();
      ((INotifyPropertyChanged)order1).PropertyChanged += (s, e) => {
        propChangedList.Add(e);
      };
      // move order
      cust2.Orders.Add(order1);

      Assert.IsTrue(cust1CollChangedList.Last().Action == NotifyCollectionChangedAction.Remove);
      Assert.IsTrue(cust1CollChangedList.Last().OldItems.Contains(order1), "change event should contain order1");

      Assert.IsTrue(cust2CollChangedList.Last().Action == NotifyCollectionChangedAction.Add);
      Assert.IsTrue(cust2CollChangedList.Last().NewItems.Contains(order1), "change event should contain order1");
      // TODO: this is not yet happening but it should
      // Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "Customer"), "propChange should mention Customer");
      Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "CustomerID"), "propChange should mention CustomerID");

      Assert.IsTrue(!cust1.Orders.Contains(order1), "order1 should have been removed");
      Assert.IsTrue(cust2.Orders.Contains(order1), "order1 should have been removed");
      Assert.IsTrue(order1.Customer == cust2, "Customer should be cust2");
      Assert.IsTrue(order1.CustomerID == cust2.CustomerID, "CustomerID should be cust2's id");
      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(cust1.Orders.Count == cust2.Orders.Count, "count should be 1");

      cust1CollChangedList.Clear();
      cust2CollChangedList.Clear();
      propChangedList.Clear();
      order1.Customer = cust1;
      Assert.IsTrue(cust1CollChangedList.Last().Action == NotifyCollectionChangedAction.Add);
      Assert.IsTrue(cust1CollChangedList.Last().NewItems.Contains(order1), "change event should contain order1");
      // Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "Customer"), "propChange should mention Customer");
      Assert.IsTrue(propChangedList.Any(args => args.PropertyName == "CustomerID"), "propChange should mention CustomerID");
      
      Assert.IsTrue(cust1.Orders.Count == 2, "count should be 2");

    }


    // graph attach (1-n) - setProperties child, attach child
    [TestMethod]
    public async Task GraphAttachChild() {
      await _emTask;

      var cust1 = new Customer();
      // this test will fail if we don't give the customer a new Guid 
      cust1.CustomerID = Guid.NewGuid();
      var order1 = new Order();
      order1.Customer = cust1;
      
      _em1.AddEntity(order1);

      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      
      Assert.IsTrue(cust1.EntityAspect.EntityState.IsAdded());
      var orders = cust1.Orders;
      Assert.IsTrue(orders.Contains(order1) , "should contain order1");
      Assert.IsTrue(order1.Customer == cust1, "Customer should be cust2");
      Assert.IsTrue(order1.CustomerID == cust1.CustomerID, "CustomerID should be cust2's id");
      

    }

    // graph attach (1-n) - setProperties child, attach child
    [TestMethod]
    public async Task GraphAttachParent() {
      await _emTask;

      var cust1 = new Customer();
      var order1 = new Order();
      cust1.EntityAspect.SetValue("Orders", new NavigationSet<Order>());
      cust1.Orders.Add(order1);

      _em1.AddEntity(cust1);

      Assert.IsTrue(order1.EntityAspect.EntityState.IsAdded());
      Assert.IsTrue(cust1.EntityAspect.EntityState.IsAdded());
      var orders = cust1.Orders;
      Assert.IsTrue(orders.Contains(order1), "should contain both orders");
      Assert.IsTrue(order1.Customer == cust1, "Customer should be cust2");
      Assert.IsTrue(order1.CustomerID == cust1.CustomerID, "CustomerID should be cust2's id");


    }

    
    [TestMethod]
    public async Task GraphAttachMultipartKey() {
      await _emTask;

      if (TestFns.DEBUG_MONGO) {
        Assert.Inconclusive("NA for Mongo - OrderDetail");
        return;
      }

      var order = new Order();
      order.OrderID = 999;
      order.EntityAspect.SetValue("OrderDetails", new NavigationSet<OrderDetail>());
      for (int i = 0; i < 3; i++) {
        var od = new OrderDetail();
        od.ProductID = i;
        order.OrderDetails.Add(od);
      }
      _em1.AttachEntity(order);
      Assert.IsTrue(order.EntityAspect.EntityState.IsUnchanged(), "order should be unchanged");
      Assert.IsTrue(order.OrderDetails.All(od => od.EntityAspect.EntityState.IsUnchanged()), "ods should all be unchanged");
      Assert.IsTrue(order.OrderDetails.Count == 3, "should be 3 ods");
      Assert.IsTrue(order.OrderDetails.All(od => od.Order == order), "should all point to order");
      Assert.IsTrue(order.OrderDetails.All(od => od.OrderID == 999), "should all have correct orderId");
      
    }

    [TestMethod]
    public async Task UnattachedChildrenMultipartkey() {
      await _emTask;

      if (TestFns.DEBUG_MONGO) {
        Assert.Inconclusive("NA for Mongo - OrderDetail");
        return;
      }

      var order = new Order();
      order.OrderID = 999;
      order.EntityAspect.SetValue("OrderDetails", new NavigationSet<OrderDetail>());
      for (int i = 0; i < 3; i++) {
        var od = new OrderDetail();
        od.ProductID = i;
        od.OrderID = order.OrderID;
        _em1.AttachEntity(od);
      }
      _em1.AttachEntity(order);
      Assert.IsTrue(order.EntityAspect.EntityState.IsUnchanged(), "order should be unchanged");
      Assert.IsTrue(order.OrderDetails.All(od => od.EntityAspect.EntityState.IsUnchanged()), "ods should all be unchanged");
      Assert.IsTrue(order.OrderDetails.Count == 3, "should be 3 ods");
      Assert.IsTrue(order.OrderDetails.All(od => od.Order == order), "should all point to order");
      Assert.IsTrue(order.OrderDetails.All(od => od.OrderID == order.OrderID), "should all have correct orderId");

    }
    
    [TestMethod]
    public async Task DuplicateKeysError() {
      await _emTask;

      var cust1 = new Customer();
      var cust2 = new Customer();
      _em1.AttachEntity(cust1);
      try {
        cust2.CustomerID = cust1.CustomerID;
        _em1.AttachEntity(cust2);
        Assert.Fail("should not get here");
      } catch (Exception e) {
        Assert.IsTrue(e.Message.Contains("key"), "message should mention 'key'");
      }
    }
    
    // fk fixup - fk to nav - attached"
    [TestMethod]
    public async Task FkFixup() {
      await _emTask;

      var cust1 = _em1.CreateEntity<Customer>(EntityState.Unchanged);
      var order1 = _em1.CreateEntity<Order>(EntityState.Unchanged);
      order1.CustomerID = cust1.CustomerID;
      Assert.IsTrue(cust1.Orders.Contains(order1), "should contain order1");
      Assert.IsTrue(order1.Customer == cust1, "customer should be attached");
    }

    // fk fixup - unattached children
    [TestMethod]
    public async Task UnattachedChildren() {
      await _emTask;

      var cust1 = new Customer();
      var cust2 = new Customer();
      var order1 = new Order();
      cust1.CustomerID = Guid.NewGuid();
      _em1.AttachEntity(order1);
      Assert.IsTrue(order1.EntityAspect.EntityState.IsUnchanged());
      order1.CustomerID = cust1.CustomerID;
      Assert.IsTrue(order1.EntityAspect.EntityState.IsModified());
      Assert.IsTrue(order1.Customer == null, "customer should be null");
      order1.EntityAspect.AcceptChanges();
      Assert.IsTrue(order1.EntityAspect.EntityState.IsUnchanged());
      _em1.AttachEntity(cust1);
      Assert.IsTrue(order1.Customer == cust1, "customer should now be set");
      Assert.IsTrue(order1.EntityAspect.EntityState.IsUnchanged(), "fixup should not change entityState");
    }

    

    // recursive navigation fixup
    [TestMethod]
    public async Task AttachRecursive() {
      await _emTask;

      var emp1 = new Employee();
      var emp2 = new Employee();
      var emp3 = new Employee();

      emp2.Manager = emp1;
      emp3.Manager = emp2;
      _em1.AttachEntity(emp3);
      Assert.IsTrue(emp3.EntityAspect.IsAttached);
      Assert.IsTrue(emp2.EntityAspect.IsAttached);
      Assert.IsTrue(emp1.EntityAspect.IsAttached);
      Assert.IsTrue(emp1.DirectReports.Contains(emp2), "emp1 manages emp2");
      Assert.IsTrue(emp2.DirectReports.Contains(emp3), "emp2 manages emp3");
      Assert.IsTrue(emp2.Manager == emp1, "emp2 manager is emp1");
      Assert.IsTrue(emp3.Manager == emp2, "emp3 mamager is emp2");

    }
  }
}
